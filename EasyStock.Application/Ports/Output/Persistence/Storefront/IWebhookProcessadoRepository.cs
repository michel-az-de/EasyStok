using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

/// <summary>
/// Repo de <see cref="WebhookProcessado"/> — pattern receive-then-process (ADR-0006).
/// Dedup via unique constraint (Provider, EventoId).
/// </summary>
public interface IWebhookProcessadoRepository
{
    Task<WebhookProcessado?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WebhookProcessado?> GetByProviderEventoAsync(string provider, string eventoId, CancellationToken ct = default);

    /// <summary>
    /// Tenta INSERT de novo webhook. Se já existe (mesma provider+evento_id), retorna
    /// (false, existente) sem lançar. Senão retorna (true, novo). Não chama SaveChanges —
    /// caller decide quando comitar (necessário para tx atômica do endpoint).
    /// </summary>
    Task<(bool inserido, WebhookProcessado registro)> TentarRegistrarRecebidoAsync(
        WebhookProcessado novo,
        CancellationToken ct = default);

    Task UpdateAsync(WebhookProcessado webhook, CancellationToken ct = default);
}
