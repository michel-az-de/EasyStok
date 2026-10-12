using EasyStock.Domain.Entities.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>F08 item 13 (#1238): o motivo da escalada persiste na conversa e volta na leitura.</summary>
[Collection("PostgreSqlTestCollection")]
public sealed class MotivoEscaladaPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task MotivoDaEscaladaVoltaNaLeitura()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var agora = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

        var conversa = Conversa.Abrir(empresaId, "5511999990123", agora, "Maria");
        conversa.Escalar(agora, "o agente não conseguiu responder");
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            db.AtendimentoConversas.Add(conversa);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        var lida = await leitura.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);
        lida.MotivoEscalada.Should().Be("o agente não conseguiu responder");
    }
}
