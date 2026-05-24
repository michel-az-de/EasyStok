using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// EF Repository de <see cref="WebhookProcessado"/>. Usa IgnoreQueryFilters em
/// quase todas as queries porque a EmpresaId é nullable (só resolvida após
/// processamento) e o handler em background opera cross-tenant.
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

        var existente = await GetByProviderEventoAsync(novo.Provider, novo.EventoId, ct);
        if (existente is not null) return (false, existente);

        db.WebhooksProcessados.Add(novo);
        return (true, novo);
    }

    public Task UpdateAsync(WebhookProcessado webhook, CancellationToken ct = default)
    {
        db.WebhooksProcessados.Update(webhook);
        return Task.CompletedTask;
    }
}
