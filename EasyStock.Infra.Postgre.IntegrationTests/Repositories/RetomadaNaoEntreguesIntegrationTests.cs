using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// S58/S59 (#1391) em Postgres real: a mensagem que espera o cliente é liberada quando ele escreve, mesmo numa
/// conversa nova com o celular noutra grafia (casa pelo cliente vinculado); o painel "Não entregues" lista as falhas.
/// </summary>
public class RetomadaNaoEntreguesIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task LiberaQuemOClienteRespondeuEListaAsNaoEntregues()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var t = DateTime.UtcNow.AddDays(-2);
        var empresa = Empresa.Criar("Casa da Baba Retomada", "11111111000272");
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Maria", Email = "m@x.com" };
        Mensagem respondida, semResposta;
        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.Add(empresa);
            db.Clientes.Add(cliente);

            // Conversa antiga sem o nono dígito, encerrada; o cliente volta noutra grafia.
            var antiga = Conversa.Abrir(empresa.Id, "551182254398", t, "Maria", cliente.Id);
            antiga.RegistrarEntrada(t);
            antiga.Encerrar(t.AddMinutes(30));
            respondida = Mensagem.Saida(empresa.Id, antiga.Id, AutorMensagem.Agente, t, TipoConteudoMensagem.Texto, "Temos ravioli!");
            respondida.RegistrarFalhaEnvio("fora da janela", TipoFalhaEnvio.Permanente, t);
            respondida.AguardarCliente(t.AddHours(25), "aguardando");
            var nova = Conversa.Abrir(empresa.Id, "5511982254398", t.AddHours(26), "Maria", cliente.Id);
            nova.RegistrarEntrada(t.AddHours(26));

            var outra = Conversa.Abrir(empresa.Id, "5511977776666", t, "João");
            outra.RegistrarEntrada(t);
            semResposta = Mensagem.Saida(empresa.Id, outra.Id, AutorMensagem.Agente, t.AddMinutes(5), TipoConteudoMensagem.Texto, "Oi João");
            semResposta.RegistrarFalhaEnvio("fora da janela", TipoFalhaEnvio.Permanente, t.AddMinutes(5));
            semResposta.AguardarCliente(t.AddHours(25), "aguardando");

            db.AtendimentoConversas.AddRange(antiga, nova, outra);
            db.AtendimentoMensagens.AddRange(respondida, semResposta);
            await db.SaveChangesAsync();
        }

        await using var dbA = fixture.CreateDbContext();
        using var bypass = dbA.UseRowLevelSecurityBypass();
        await using var tx = await dbA.Database.BeginTransactionAsync();
        var repo = new ConversaRepository(dbA);

        var liberadas = await repo.ListarAguardandoComRespostaComLockAsync(10);
        liberadas.Select(m => m.Id).Should().Contain(respondida.Id).And.NotContain(semResposta.Id);

        // As consultas LINQ rodam no escopo da empresa (filtro global), como no console e no reenvio.
        dbA.SetMobileTenantContext(empresa.Id);
        (await repo.ExisteAguardandoClienteAsync(empresa.Id, CanalConversa.WhatsApp, "5511982254398", cliente.Id, t))
            .Should().BeTrue("o cliente vinculado cobre a outra grafia do celular");
        (await repo.ExisteAguardandoClienteAsync(empresa.Id, CanalConversa.WhatsApp, "5511900000000", null, t))
            .Should().BeFalse();

        var naoEntregues = await repo.ListarNaoEntreguesAsync(empresa.Id, 10);
        naoEntregues.Select(l => l.Mensagem.Id).Should().Equal(semResposta.Id, respondida.Id);
        naoEntregues[0].Conversa.ContatoNome.Should().Be("João");
        await tx.RollbackAsync();
    }
}
