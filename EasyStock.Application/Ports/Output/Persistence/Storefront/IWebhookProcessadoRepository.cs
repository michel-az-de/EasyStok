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
    /// (false, existente) sem lançar. Senão retorna (true, novo).
    ///
    /// <para>
    /// <strong>Chama SaveChanges internamente</strong> — atomicidade do dedup depende
    /// da unique constraint <c>uq_webhook_processado_provider_evento</c> disparar
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>, o que só acontece
    /// no flush. O contrato "receive" do ADR-0006 é uma transação curta independente
    /// do processamento subsequente, então isso casa com o pattern.
    /// </para>
    /// </summary>
    Task<(bool inserido, WebhookProcessado registro)> TentarRegistrarRecebidoAsync(
        WebhookProcessado novo,
        CancellationToken ct = default);

    Task UpdateAsync(WebhookProcessado webhook, CancellationToken ct = default);
}
