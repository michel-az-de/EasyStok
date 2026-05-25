using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// EF Repository de <see cref="WebhookProcessado"/>. Usa IgnoreQueryFilters em
/// quase todas as queries porque a EmpresaId é nullable (só resolvida após
/// processamento) e o handler em background opera cross-tenant.
///
/// <para>
/// Dedup do <see cref="TentarRegistrarRecebidoAsync"/> é via unique constraint
/// <c>uq_webhook_processado_provider_evento</c> + catch de
/// <see cref="DbUpdateException"/> com SQLSTATE 23505 — mesmo pattern de
/// <see cref="EasyStock.Infra.Postgre.Repositories.WebhookRecebidoRepository"/>.
/// </para>
/// </summary>
public sealed class WebhookProcessadoRepository(EasyStockDbContext db) : IWebhookProcessadoRepository
{
    public Task<WebhookProcessado?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.WebhooksProcessados
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<WebhookProcessado?> GetByProviderEventoAsync(
        string provider,
        string eventoId,
        CancellationToken ct = default) =>
        db.WebhooksProcessados
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Provider == provider && w.EventoId == eventoId, ct);

    public async Task<(bool inserido, WebhookProcessado registro)> TentarRegistrarRecebidoAsync(
        WebhookProcessado novo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(novo);

        try
        {
            await db.WebhooksProcessados.AddAsync(novo, ct);
            await db.SaveChangesAsync(ct);
            return (true, novo);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Outro request (ou retry do provider) já registrou este (provider, eventoId).
            // Detacha pra não tentar novamente em próximos SaveChanges, busca o vencedor.
            db.Entry(novo).State = EntityState.Detached;
            var existente = await GetByProviderEventoAsync(novo.Provider, novo.EventoId, ct);
            return (false, existente ?? throw new InvalidOperationException(
                $"Unique violation em (provider={novo.Provider}, eventoId={novo.EventoId}) mas registro existente não encontrado."));
        }
    }

    public Task UpdateAsync(WebhookProcessado webhook, CancellationToken ct = default)
    {
        db.WebhooksProcessados.Update(webhook);
        return Task.CompletedTask;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505";
}
