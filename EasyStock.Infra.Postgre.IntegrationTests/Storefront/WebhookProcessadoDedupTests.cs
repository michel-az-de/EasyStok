using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// Testa dedup atômico de webhooks via unique constraint (provider, evento_id) — ADR-0006.
/// Concorrência 100x: 100 requests paralelos com mesmo payload → 1 INSERT, 100 retornam sucesso.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class WebhookProcessadoDedupTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task TentarRegistrar_100RequestsParalelosMesmoEvento_ExatamenteUmInserido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        // ── Setup limpo ─────────────────────────────────────────────────────
        await using var dbSetup = fixture.CreateDbContext();
        await dbSetup.Database.MigrateAsync();

        const string provider = "mercadopago";
        var eventoId = $"webhook-dedup-{Guid.NewGuid():N}";
        var payloadRaw = $"{{\"data\":{{\"id\":\"{eventoId}\"}}}}";

        // ── Execução concorrente: 100 tentativas paralelas com mesmo (provider, eventoId) ──
        const int concorrentes = 100;
        var tasks = Enumerable.Range(0, concorrentes).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var repo = new WebhookProcessadoRepository(db);

            var webhook = WebhookProcessado.Receber(
                provider: provider,
                eventoId: eventoId,
                tipo: "payment.updated",
                payloadRaw: payloadRaw);

            var (inserido, _) = await repo.TentarRegistrarRecebidoAsync(webhook);
            return inserido;
        })).ToList();

        var results = await Task.WhenAll(tasks);

        // ── Asserções ───────────────────────────────────────────────────────
        var inseridos = results.Count(r => r);
        var duplicados = results.Count(r => !r);

        inseridos.Should().Be(1,
            "exatamente 1 INSERT atômico vencer a corrida — demais devem capturar UniqueConstraintViolation");
        duplicados.Should().Be(concorrentes - 1,
            $"os {concorrentes - 1} restantes devem voltar como 'duplicado' (não-inserido) silenciosamente");

        // ── Verificação final: tabela tem 1 linha para este (provider, eventoId) ──
        await using var dbCheck = fixture.CreateDbContext();
        var count = await dbCheck.WebhooksProcessados
            .IgnoreQueryFilters()
            .CountAsync(w => w.Provider == provider && w.EventoId == eventoId);
        count.Should().Be(1, "unique constraint garante que apenas 1 linha persiste");
    }

    [SkippableFact]
    public async Task TentarRegistrar_DoisProvidersDiferentesMesmoEventoId_AmbosInseridos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        await using var dbSetup = fixture.CreateDbContext();
        await dbSetup.Database.MigrateAsync();

        var eventoId = $"event-{Guid.NewGuid():N}";

        await using var dbMp = fixture.CreateDbContext();
        var repoMp = new WebhookProcessadoRepository(dbMp);
        var webhookMp = WebhookProcessado.Receber("mercadopago", eventoId, "payment.updated", "{}");
        var (inserMp, _) = await repoMp.TentarRegistrarRecebidoAsync(webhookMp);

        await using var dbEfi = fixture.CreateDbContext();
        var repoEfi = new WebhookProcessadoRepository(dbEfi);
        var webhookEfi = WebhookProcessado.Receber("efi", eventoId, "pix.received", "{}");
        var (inserEfi, _) = await repoEfi.TentarRegistrarRecebidoAsync(webhookEfi);

        inserMp.Should().BeTrue();
        inserEfi.Should().BeTrue("unique constraint é (provider, eventoId) — providers diferentes não colidem");
    }

    [SkippableFact]
    public async Task TentarRegistrar_MesmoProviderEventoIdsDiferentes_AmbosInseridos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        await using var dbSetup = fixture.CreateDbContext();
        await dbSetup.Database.MigrateAsync();

        await using var db = fixture.CreateDbContext();
        var repo = new WebhookProcessadoRepository(db);

        var w1 = WebhookProcessado.Receber("mercadopago", $"e1-{Guid.NewGuid():N}", "payment.updated", "{}");
        var w2 = WebhookProcessado.Receber("mercadopago", $"e2-{Guid.NewGuid():N}", "payment.updated", "{}");

        var (i1, _) = await repo.TentarRegistrarRecebidoAsync(w1);
        var (i2, _) = await repo.TentarRegistrarRecebidoAsync(w2);

        i1.Should().BeTrue();
        i2.Should().BeTrue();
    }
}
