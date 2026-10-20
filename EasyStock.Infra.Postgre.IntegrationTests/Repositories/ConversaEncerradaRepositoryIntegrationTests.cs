using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>#1765-5: a entrada depois do encerramento procura a última conversa encerrada do contato.</summary>
public class ConversaEncerradaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task UltimaEncerradaPorContato_EAMaisRecenteDoMesmoContato()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        const string wa = "5511999990002";

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var antiga = Conversa.Abrir(empresa, wa, Agora);
        antiga.Encerrar(Agora.AddMinutes(10));
        var recente = Conversa.Abrir(empresa, wa, Agora.AddMinutes(20));
        recente.DefinirPedidoEmAndamento(Guid.NewGuid());
        recente.Encerrar(Agora.AddMinutes(30));
        var outroContato = Conversa.Abrir(empresa, "5511999990003", Agora.AddMinutes(40));
        outroContato.Encerrar(Agora.AddMinutes(50));
        db.AtendimentoConversas.AddRange(antiga, recente, outroContato);
        await db.SaveChangesAsync();

        var repo = new ConversaRepository(db);
        var ultima = await repo.ObterUltimaEncerradaPorContatoAsync(empresa, CanalConversa.WhatsApp, wa);

        ultima.Should().NotBeNull();
        ultima!.Id.Should().Be(recente.Id, "a mais recente do contato, com o pedido em andamento");
        (await repo.ObterUltimaEncerradaPorContatoAsync(empresa, CanalConversa.WhatsApp, "5511999990009")).Should().BeNull();
    }
}
