using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;

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

        soma.Should().Be(new SomaTurnosAgente(2, 300, 50, 50, 0.001m, 1, RespostasSemIa: 0, TurnosComTokens: 2));
    }

    [SkippableFact]
    public async Task Soma_conta_como_resposta_sem_IA_so_o_desfecho_SemIa()
    {
        // S78 (#2896): um turno de cada desfecho e dois sem IA. Todos são turnos; só os dois entram como sem IA.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid(); var conversa = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await Gravar(empresa,
            Turno(empresa, conversa, em, 100, 20, 0, 0.001m, DesfechoTurnoAgente.Respondeu),
            Turno(empresa, conversa, em, 100, 20, 0, 0.001m, DesfechoTurnoAgente.Escalou),
            Turno(empresa, conversa, em, 0, 0, 0, null, DesfechoTurnoAgente.Cota),
            Turno(empresa, conversa, em, 0, 0, 0, null, DesfechoTurnoAgente.Erro),
            Turno(empresa, conversa, em, 100, 20, 0, 0.001m, DesfechoTurnoAgente.Interrompido),
            Turno(empresa, conversa, em, 0, 0, 0, null, DesfechoTurnoAgente.SemIa),
            Turno(empresa, conversa, em, 0, 0, 0, null, DesfechoTurnoAgente.SemIa),
            // Fora da janela: não conta.
            Turno(empresa, conversa, new DateTime(2026, 11, 1, 3, 30, 0, DateTimeKind.Utc), 0, 0, 0, null, DesfechoTurnoAgente.SemIa));
        await Gravar(outra, Turno(outra, Guid.NewGuid(), em, 0, 0, 0, null, DesfechoTurnoAgente.SemIa));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        soma.Turnos.Should().Be(7);
        soma.RespostasSemIa.Should().Be(2);
        // Respondeu, Escalou e Interrompido gastaram token; Cota, Erro e os dois SemIa, não.
        soma.TurnosComTokens.Should().Be(3);
    }

    [SkippableFact]
    public async Task Soma_do_uso_deixa_de_fora_o_turno_de_portao()
    {
        // #3175: turno de portão não chama o modelo. No uso do mês ele não é turno nem "falta de preço": se entrasse só
        // num dos dois, a tela deixaria de avisar que o modelo está sem preço. Motivo que não é de portão continua contando.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var conversa = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await Gravar(empresa,
            Turno(empresa, conversa, em, 100, 20, 0, null, DesfechoTurnoAgente.Respondeu),
            TurnoAgente.Registrar(empresa, conversa, null, null, "claude-sonnet-5", 100, 20, 0, null, [], 1, DesfechoTurnoAgente.Escalou, 0, em, "guarda:outro"),
            TurnoAgente.Registrar(empresa, conversa, null, null, null, 0, 0, 0, null, [], 0, DesfechoTurnoAgente.Escalou, 0, em, "portao:chave"),
            TurnoAgente.Registrar(empresa, conversa, null, null, null, 0, 0, 0, null, [], 0, DesfechoTurnoAgente.Cota, 0, em, "portao:cota"));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        soma.Turnos.Should().Be(2, "os dois que chamaram o modelo");
        soma.TurnosSemPreco.Should().Be(2, "sem preço é todo turno contado: a tela compara os dois números");
    }

    [SkippableFact]
    public async Task Historico_grava_e_le_o_motivo_do_turno()
    {
        // #3175: coluna nova, anulavel: o turno que respondeu normalmente fica sem motivo.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var agora = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var conversa = Guid.NewGuid();
        var barrado = TurnoAgente.Registrar(empresa, conversa, null, null, null, 0, 0, 0, null, [], 0,
            DesfechoTurnoAgente.Escalou, 0, agora.AddMinutes(1), "portao:limite");
        var normal = Turno(empresa, conversa, agora, 10, 0, 0, null, DesfechoTurnoAgente.Respondeu);

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.TurnosAgente.AddRange(barrado, normal);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext(); leitura.SetMobileTenantContext(empresa);
        var pagina = await new TurnoAgenteRepository(leitura).ListarAsync(empresa, new FiltroTurnosAgente(conversa, null, 1, 10));

        pagina.Itens.Select(t => t.Motivo).Should().Equal("portao:limite", null);
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

    // #3370 (D2 da #3271): o consumo é o maior entre os turnos e as chamadas ligadas a eles em chamada_ia. O papel (superuser)
    // dos testes de soma não aplica RLS: o que filtra a empresa aqui é a consulta; o teste de RLS abaixo usa o papel sujeito a ela.

    [SkippableFact]
    public async Task Piso_poe_na_soma_os_tokens_do_turno_que_nao_foi_gravado()
    {
        // O turno caiu depois de chamar o modelo e a linha dele não nasceu: as duas chamadas dele ficaram em chamada_ia.
        // Os turnos gravados (um do modelo, um sem IA) têm cada um as chamadas iguais ao que gravaram.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var conversa = Guid.NewGuid(); var caiu = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var respondeu = Turno(empresa, conversa, em, 100, 20, 50, 0.001m, DesfechoTurnoAgente.Respondeu);
        await Gravar(empresa, respondeu, Turno(empresa, conversa, em, 0, 0, 0, null, DesfechoTurnoAgente.SemIa));
        await GravarChamadas(empresa,
            Chamada(empresa, respondeu.Id, em, 100, 20, 50),
            Chamada(empresa, caiu, em.AddSeconds(5), 500, 100, 200),
            Chamada(empresa, caiu, em.AddSeconds(9), 300, 50, 0));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        // Tokens das chamadas (170 do turno gravado + 1.150 do que caiu); o resto continua sendo o dos turnos.
        soma.Should().Be(new SomaTurnosAgente(2, 900, 170, 250, 0.001m, 1, RespostasSemIa: 1, TurnosComTokens: 1)
        {
            TokensDosTurnos = 170,
        });
        soma.TotalTokens.Should().Be(1_320);
        soma.TokensPoupadosEstimados.Should().Be(170, "a média usa os tokens dos turnos, não o piso");
    }

    [SkippableFact]
    public async Task Piso_nao_baixa_a_soma_quando_a_chamada_foi_perdida()
    {
        // chamada_ia perdeu a linha de um turno (o registro engole a falha com log): os turnos têm mais do que as chamadas.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var conversa = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var primeiro = Turno(empresa, conversa, em, 1_000, 200, 0, 0.01m, DesfechoTurnoAgente.Respondeu);
        var perdido = Turno(empresa, conversa, em, 400, 100, 0, 0.004m, DesfechoTurnoAgente.Respondeu);
        await Gravar(empresa, primeiro, perdido);
        await GravarChamadas(empresa, Chamada(empresa, primeiro.Id, em, 1_000, 200, 0));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        soma.Should().Be(new SomaTurnosAgente(2, 1_400, 300, 0, 0.014m, 0, TurnosComTokens: 2));
        soma.TokensDosTurnos.Should().BeNull();
    }

    [SkippableFact]
    public async Task Piso_com_turnos_e_chamadas_iguais_nao_conta_duas_vezes()
    {
        // O caso de todo dia: cada turno e as chamadas dele batem (a escrita de cache já dentro da entrada do turno, #2862) e
        // o empate não conta duas vezes. Que a escrita de cache entra na entrada das chamadas, quem prova é o teste da janela.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var conversa = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var primeiro = Turno(empresa, conversa, em, 140, 20, 50, 0.001m, DesfechoTurnoAgente.Respondeu);
        var segundo = Turno(empresa, conversa, em, 200, 30, 0, 0.002m, DesfechoTurnoAgente.Escalou);
        await Gravar(empresa, primeiro, segundo);
        await GravarChamadas(empresa,
            Chamada(empresa, primeiro.Id, em, 100, 20, 50, cacheEscrito: 40),
            Chamada(empresa, segundo.Id, em, 120, 10, 0),
            Chamada(empresa, segundo.Id, em.AddSeconds(3), 80, 20, 0));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        soma.Should().Be(new SomaTurnosAgente(2, 340, 50, 50, 0.003m, 0, TurnosComTokens: 2));
    }

    [SkippableFact]
    public async Task Piso_conta_so_a_chamada_do_Assistente_ligada_a_turno_da_empresa_e_da_janela()
    {
        // Sem nenhum turno gravado, só entra o que é consumo do Assistente neste mês. As "fora" têm tokens enormes: qualquer
        // filtro de janela, turno ou finalidade que falte aparece na soma. A outra empresa já some pelo filtro global do EF; o
        // filtro de empresa da própria consulta é o do teste seguinte.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid(); var turno = Guid.NewGuid();
        var dentro = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await GravarChamadas(empresa,
            Chamada(empresa, turno, InicioOutubroUtc, 100, 10, 0),
            Chamada(empresa, turno, dentro, 200, 20, 5, cacheEscrito: 40),
            // Fora: um instante antes da janela, exatamente no fim dela, sugestão para a equipe (sem turno) e outra finalidade.
            Chamada(empresa, turno, InicioOutubroUtc.AddMilliseconds(-1), 9_000, 9_000, 9_000),
            Chamada(empresa, turno, FimOutubroUtc, 9_000, 9_000, 9_000),
            Chamada(empresa, null, dentro, 9_000, 9_000, 9_000),
            Chamada(empresa, turno, dentro, 9_000, 9_000, 9_000, finalidade: FinalidadeIa.ResumoCliente));
        await GravarChamadas(outra, Chamada(outra, turno, dentro, 9_000, 9_000, 9_000));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        // A escrita de cache entra na entrada (200 + 40), como o turno soma.
        (soma.TokensEntrada, soma.TokensSaida, soma.TokensCacheLido).Should().Be((340L, 30L, 5L));
        soma.Turnos.Should().Be(0);
    }

    [SkippableFact]
    public async Task Piso_conta_a_chamada_que_gastou_token_seja_qual_for_o_desfecho()
    {
        // O desfecho não filtra: o que foi registrado com token conta, mesmo que a chamada tenha terminado em erro, e a que
        // nem saiu (cota estourada) vem com zero e não muda a soma.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var turno = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await GravarChamadas(empresa,
            Chamada(empresa, turno, em, 100, 10, 0),
            Chamada(empresa, turno, em.AddSeconds(2), 50, 5, 0, desfecho: DesfechoChamadaIa.Erro),
            Chamada(empresa, turno, em.AddSeconds(4), 0, 0, 0, desfecho: DesfechoChamadaIa.CotaEstourada));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        (soma.TokensEntrada, soma.TokensSaida, soma.TokensCacheLido).Should().Be((150L, 15L, 0L));
    }

    [SkippableFact]
    public async Task Piso_soma_em_long_e_nao_estoura_o_int()
    {
        // Duas chamadas de 1,5 bilhão de tokens de entrada somam mais do que int.MaxValue. Sem a conversão para long na
        // consulta o banco devolve um bigint que não cabe no int do LINQ e a cota nem calcula.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var turno = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await GravarChamadas(empresa,
            Chamada(empresa, turno, em, 1_500_000_000, 0, 0),
            Chamada(empresa, turno, em.AddSeconds(5), 1_500_000_000, 0, 0));

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        soma.TokensEntrada.Should().Be(3_000_000_000L);
    }

    [SkippableFact]
    public async Task Piso_filtra_a_empresa_na_propria_consulta_sem_o_filtro_global_do_EF()
    {
        // O SuperAdmin desliga o filtro global de tenant e o papel dos testes não aplica RLS: sobra a empresa que a consulta
        // do piso passa. Sem ela a soma de uma loja levaria as chamadas de todas as outras.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await GravarChamadas(empresa, Chamada(empresa, Guid.NewGuid(), em, 10, 5, 0));
        await GravarChamadas(outra, Chamada(outra, Guid.NewGuid(), em, 9_000, 9_000, 0));
        var admin = Substitute.For<ICurrentUserAccessor>();
        admin.IsAuthenticated.Returns(true);
        admin.Nivel.Returns(NivelAcesso.SuperAdmin);

        await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.ConnectionString).Options, admin);
        (await db.ChamadasIa.Where(c => c.EmpresaId == outra).CountAsync()).Should().Be(1, "o filtro global está desligado");
        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        soma.TotalTokens.Should().Be(15);
    }

    [SkippableFact]
    public async Task Rls_esconde_chamada_de_outra_empresa_e_o_piso_le_a_propria()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var em = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await GravarChamadas(empresa, Chamada(empresa, Guid.NewGuid(), em, 10, 5, 0));
        await GravarChamadas(outra, Chamada(outra, Guid.NewGuid(), em, 9_000, 9_000, 0));

        // Papel NOBYPASSRLS: a policy esconde a outra loja mesmo sem o filtro do EF, e o piso ainda lê a própria. Não mata
        // mutante da consulta (ela filtra a empresa sozinha): prova que a policy de chamada_ia deixa o piso ler a própria loja.
        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.ToString()}, false)");
        (await db.ChamadasIa.IgnoreQueryFilters().Select(c => c.EmpresaId).Distinct().ToListAsync()).Should().Equal(empresa);
        db.SetMobileTenantContext(empresa);

        var soma = await new TurnoAgenteRepository(db).SomarAsync(empresa, InicioOutubroUtc, FimOutubroUtc);

        soma.TotalTokens.Should().Be(15);
    }

    [SkippableFact]
    public async Task Cota_barra_o_turno_seguinte_com_os_tokens_de_um_turno_que_nao_foi_gravado()
    {
        // A regressão que a fatia existe para fechar: o turno caiu depois de chamar o modelo, não deixou linha, e a cota
        // do mês via só os turnos gravados. Agora as chamadas dele entram e a etapa da cota barra o turno seguinte.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var agora = new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc);
        await GravarChamadas(empresa, Chamada(empresa, Guid.NewGuid(), agora.AddMinutes(-5), 700, 200, 150));
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(empresa);
        configuracao.DefinirAssistente(null, null, 1_000, null, null);

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        var turnos = new TurnoAgenteRepository(db);
        var portoes = new PortoesDoAssistente(Substitute.For<IAgenteLlmClient>(), Substitute.For<IConversaRepository>(), turnos,
            Options.Create(new LimiteRespostasAgenteOptions { MaximoPorHora = 10 }));

        (await PortoesDoAssistente.UsadosNoMesAsync(turnos, empresa, agora, default)).Should().Be(1_050);
        var etapa = await portoes.DaCotaAsync(empresa, configuracao, agora, default);

        etapa.Barra.Should().BeTrue();
        etapa.MotivoEscalada.Should().Be(PortoesDoAssistente.MotivoCotaEsgotada);
    }

    private static TurnoAgente Turno(Guid empresa, Guid conversa, DateTime em, int entrada, int saida, int cache, decimal? custo,
        DesfechoTurnoAgente desfecho) =>
        TurnoAgente.Registrar(empresa, conversa, null, null, "claude-sonnet-5", entrada, saida, cache, custo, [], 1, desfecho, 800, em);

    private static ChamadaIa Chamada(Guid empresa, Guid? turno, DateTime em, int entrada, int saida, int cacheLido,
        int cacheEscrito = 0, FinalidadeIa finalidade = FinalidadeIa.Atendimento, DesfechoChamadaIa desfecho = DesfechoChamadaIa.Ok) =>
        ChamadaIa.Registrar(empresa, null, null, "claude-sonnet-5", finalidade, turno, entrada, saida, cacheLido, cacheEscrito, 0.001m,
            800, desfecho, em);

    private async Task GravarChamadas(Guid empresa, params ChamadaIa[] chamadas)
    {
        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        db.ChamadasIa.AddRange(chamadas);
        await db.SaveChangesAsync();
    }

    private async Task Gravar(Guid empresa, params TurnoAgente[] turnos)
    {
        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        db.TurnosAgente.AddRange(turnos);
        await db.SaveChangesAsync();
    }
}
