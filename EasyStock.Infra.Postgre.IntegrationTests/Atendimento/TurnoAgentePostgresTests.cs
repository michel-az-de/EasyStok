using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>S67 (#2534): registro dos turnos do Assistente em Postgres real.</summary>
[Collection("PostgreSqlTestCollection")]
public sealed class TurnoAgentePostgresTests(PostgreSqlDatabaseFixture fixture)
{
    // Outubro de 2026 em Brasília vai de 01/10 03h UTC a 01/11 03h UTC.
    private static readonly DateTime InicioOutubroUtc = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FimOutubroUtc = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Soma_do_mes_conta_so_a_empresa_e_a_janela_de_Brasilia()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid(); var conversa = Guid.NewGuid();
        await Gravar(empresa,
            Turno(empresa, conversa, new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), 100, 20, 50, 0.001m, DesfechoTurnoAgente.Respondeu),
            Turno(empresa, conversa, new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), 200, 30, 0, null, DesfechoTurnoAgente.Escalou),
            // 01/10 às 2h UTC ainda é setembro em Brasília; 01/11 às 3h30 UTC já é novembro.
            Turno(empresa, conversa, new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc), 7_000, 7_000, 0, 1m, DesfechoTurnoAgente.Respondeu),
            Turno(empresa, conversa, new DateTime(2026, 11, 1, 3, 30, 0, DateTimeKind.Utc), 9_000, 9_000, 0, 1m, DesfechoTurnoAgente.Respondeu));
        await Gravar(outra,
            Turno(outra, Guid.NewGuid(), new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc), 5_000, 5_000, 0, 1m, DesfechoTurnoAgente.Respondeu));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        soma.Should().Be(new SomaTurnosAgente(2, 300, 50, 50, 0.001m, 1));
    }

    [SkippableFact]
    public async Task Historico_traz_pergunta_e_resposta_do_mais_novo_ao_mais_velho_com_filtros()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var agora = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var conversa = Conversa.Abrir(empresa, Guid.NewGuid().ToString("N"), agora, canal: CanalConversa.ChatSite);
        var pergunta = Mensagem.Entrada(empresa, conversa.Id, agora, TipoConteudoMensagem.Texto, "quanto custa o bolo?");
        var resposta = Mensagem.Saida(empresa, conversa.Id, AutorMensagem.Agente, agora.AddSeconds(5), TipoConteudoMensagem.Texto,
            "Custa R$ 80.", "turno:" + Guid.NewGuid().ToString("N"));
        var respondido = TurnoAgente.Registrar(empresa, conversa.Id, pergunta.Id, resposta.Id, "claude-sonnet-5", 100, 20, 0, 0.001m,
            [new FerramentaDoTurno("consultar_cardapio", true)], 2, DesfechoTurnoAgente.Respondeu, 1_500, agora.AddSeconds(5));
        var escalado = Turno(empresa, conversa.Id, agora.AddMinutes(1), 50, 0, 0, null, DesfechoTurnoAgente.Escalou);
        var deOutraConversa = Turno(empresa, Guid.NewGuid(), agora.AddMinutes(2), 10, 0, 0, null, DesfechoTurnoAgente.Respondeu);

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.AtendimentoConversas.Add(conversa);
            db.AtendimentoMensagens.AddRange(pergunta, resposta);
            db.TurnosAgente.AddRange(respondido, escalado, deOutraConversa);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext(); leitura.SetMobileTenantContext(empresa);
        var repo = new TurnoAgenteRepository(leitura);

        var todos = await repo.ListarAsync(empresa, new FiltroTurnosAgente(null, null, 1, 10));
        todos.Total.Should().Be(3);
        todos.Itens.Select(t => t.Id).Should().Equal(deOutraConversa.Id, escalado.Id, respondido.Id);
        var lido = todos.Itens[^1];
        lido.PerguntaCliente.Should().Be("quanto custa o bolo?");
        lido.Resposta.Should().Be("Custa R$ 80.");
        lido.Ferramentas.Should().Equal(new FerramentaDoTurno("consultar_cardapio", true));
        lido.CustoEstimado.Should().Be(0.001m);

        var daConversa = await repo.ListarAsync(empresa, new FiltroTurnosAgente(conversa.Id, null, 1, 1));
        daConversa.Total.Should().Be(2);
        daConversa.Itens.Select(t => t.Id).Should().Equal(escalado.Id);

        var escalados = await repo.ListarAsync(empresa, new FiltroTurnosAgente(null, DesfechoTurnoAgente.Escalou, 1, 10));
        escalados.Itens.Select(t => t.Id).Should().Equal(escalado.Id);
        escalados.Itens[0].PerguntaCliente.Should().BeNull();
    }

    [SkippableFact]
    public async Task Rls_esconde_turno_de_outra_empresa_mesmo_sem_filtro_ef()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var agora = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await Gravar(empresa, Turno(empresa, Guid.NewGuid(), agora, 1, 1, 0, null, DesfechoTurnoAgente.Respondeu));
        await Gravar(outra, Turno(outra, Guid.NewGuid(), agora, 1, 1, 0, null, DesfechoTurnoAgente.Respondeu));

        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.ToString()}, false)");
        (await db.TurnosAgente.IgnoreQueryFilters().Select(t => t.EmpresaId).Distinct().ToListAsync()).Should().Equal(empresa);
        db.TurnosAgente.Add(Turno(outra, Guid.NewGuid(), agora, 1, 1, 0, null, DesfechoTurnoAgente.Respondeu));
        await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    private static TurnoAgente Turno(Guid empresa, Guid conversa, DateTime em, int entrada, int saida, int cache, decimal? custo,
        DesfechoTurnoAgente desfecho) =>
        TurnoAgente.Registrar(empresa, conversa, null, null, "claude-sonnet-5", entrada, saida, cache, custo, [], 1, desfecho, 800, em);

    private async Task Gravar(Guid empresa, params TurnoAgente[] turnos)
    {
        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        db.TurnosAgente.AddRange(turnos);
        await db.SaveChangesAsync();
    }
}
