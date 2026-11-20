using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>#2921: conversa aberta so para montar pedido na tela (sem mensagem da cliente) nao entra na fila; com busca, entra.</summary>
public class InboxConversaEscondidaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ConversaSemEntradaDaClienteFicaForaDaFila_ESoApareceNaBusca()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Fila escondida", null);
        var escondida = Conversa.Abrir(empresa.Id, "5511988880001", agora, contatoNome: "Marina Escondida");
        var escreveu = Conversa.Abrir(empresa.Id, "5511988880002", agora.AddSeconds(-1), contatoNome: "Paula Visivel");
        escreveu.RegistrarEntrada(agora);
        await using (var seed = fixture.CreateDbContext())
        {
            using var bypass = seed.UseRowLevelSecurityBypass();
            seed.Empresas.Add(empresa);
            seed.AtendimentoConversas.AddRange(escondida, escreveu);
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var repo = new ConversaRepository(db);

        (await repo.ListarInboxAsync(empresa.Id, null, null, null, 1, 100))
            .Should().ContainSingle().Which.Id.Should().Be(escreveu.Id);
        (await repo.ListarInboxAsync(empresa.Id, null, "Escondida", null, 1, 100))
            .Should().ContainSingle().Which.Id.Should().Be(escondida.Id, "a busca encontra a conversa escondida");
    }
}
