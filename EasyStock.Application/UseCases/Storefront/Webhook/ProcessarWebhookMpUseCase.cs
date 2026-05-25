using System.Diagnostics;
using EasyStock.Application.Events.Storefront;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Events.Storefront;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Storefront.Webhook;

/// <summary>
/// Resultado do <see cref="ProcessarWebhookMpUseCase"/>.
/// </summary>
public enum ProcessarWebhookMpResultado
{
    /// <summary>Pagamento aprovado, Pedido → AguardandoAprovacaoBaba.</summary>
    Aprovado = 0,

    /// <summary>Pagamento recusado/cancelado, Pedido → Cancelado, vaga liberada.</summary>
    Recusado = 1,

    /// <summary>Pagamento ainda pendente — agendar nova tentativa.</summary>
    Pendente = 2,

    /// <summary>External_reference sem Pedido correspondente — webhook órfão.</summary>
    Orfao = 3,

    /// <summary>Erro inesperado — webhook fica em status=Error pra retry com backoff.</summary>
    Erro = 4,

    /// <summary>Webhook já não está em status Received (foi processado por outro worker).</summary>
    JaProcessado = 5,
}

/// <summary>
/// Processamento assíncrono de webhooks MercadoPago (ADR-0006 §Process).
///
/// <para>
/// Idempotente: lê <see cref="WebhookProcessado"/> em <see cref="WebhookProcessadoStatus.Received"/>,
/// chama <see cref="IMercadoPagoClient.GetPaymentAsync"/> como fonte da verdade
/// (NÃO confia no payload do webhook), atualiza o Pedido correspondente.
/// </para>
///
/// <para>
/// <strong>Status MP canônicos:</strong>
/// <list type="bullet">
///   <item><c>approved</c> → <see cref="ProcessarWebhookMpResultado.Aprovado"/>.</item>
///   <item><c>rejected</c>, <c>cancelled</c>, <c>charged_back</c>, <c>refunded</c> → <see cref="ProcessarWebhookMpResultado.Recusado"/>.</item>
///   <item><c>pending</c>, <c>in_process</c>, <c>authorized</c> → <see cref="ProcessarWebhookMpResultado.Pendente"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ProcessarWebhookMpUseCase(
    IWebhookProcessadoRepository webhookRepository,
    IMercadoPagoClient mercadoPagoClient,
    IPedidoStorefrontRepository pedidoRepository,
    IVagaOcupadaRepository vagaOcupadaRepository,
    IPublicadorEventos publicadorEventos,
    ILogger<ProcessarWebhookMpUseCase> logger)
{
    public async Task<ProcessarWebhookMpResultado> ExecuteAsync(
        Guid webhookProcessadoId,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var webhook = await webhookRepository.GetByIdAsync(webhookProcessadoId, ct);
        if (webhook is null)
        {
            logger.LogWarning(
                "processar_webhook_mp_nao_encontrado webhookId={WebhookId}", webhookProcessadoId);
            return ProcessarWebhookMpResultado.Erro;
        }

        if (webhook.Status != WebhookProcessadoStatus.Received)
        {
            logger.LogDebug(
                "processar_webhook_mp_ja_processado webhookId={WebhookId} status={Status}",
                webhookProcessadoId, webhook.Status);
            return ProcessarWebhookMpResultado.JaProcessado;
        }

        // ── 1) Consultar MP (fonte da verdade) ─────────────────────────────
        MpPaymentDetailsDto detalhes;
        try
        {
            detalhes = await mercadoPagoClient.GetPaymentAsync(webhook.EventoId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // cancellation propaga
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "processar_webhook_mp_erro_get_payment webhookId={WebhookId} eventoId={EventoId}",
                webhookProcessadoId, webhook.EventoId);
            try { webhook.MarcarErro($"GetPayment falhou: {ex.GetType().Name}"); }
            catch { /* já em status terminal */ }
            await webhookRepository.UpdateAsync(webhook, ct);
            return ProcessarWebhookMpResultado.Erro;
        }

        // ── 2) Resolver Pedido via external_reference ──────────────────────
        if (string.IsNullOrWhiteSpace(detalhes.ExternalReference)
            || !Guid.TryParse(detalhes.ExternalReference, out var pedidoId))
        {
            logger.LogWarning(
                "processar_webhook_mp_external_ref_invalida webhookId={WebhookId} ref={Ref}",
                webhookProcessadoId, detalhes.ExternalReference);
            webhook.MarcarOrfao($"external_reference inválida: '{detalhes.ExternalReference}'");
            await webhookRepository.UpdateAsync(webhook, ct);
            return ProcessarWebhookMpResultado.Orfao;
        }

        var pedido = await pedidoRepository.GetByIdAsync(pedidoId, ct);
        if (pedido is null)
        {
            logger.LogWarning(
                "processar_webhook_mp_pedido_inexistente webhookId={WebhookId} pedidoId={PedidoId}",
                webhookProcessadoId, pedidoId);
            webhook.MarcarOrfao($"Pedido {pedidoId} não encontrado");
            await webhookRepository.UpdateAsync(webhook, ct);
            return ProcessarWebhookMpResultado.Orfao;
        }

        // ── 3) Aplicar conforme status canônico do MP ──────────────────────
        var statusNormalizado = (detalhes.Status ?? string.Empty).Trim().ToLowerInvariant();

        try
        {
            switch (statusNormalizado)
            {
                case "approved":
                    await AplicarAprovadoAsync(webhook, pedido, ct);
                    logger.LogInformation(
                        "processar_webhook_mp_aprovado webhookId={WebhookId} pedidoId={PedidoId} elapsedMs={Ms}",
                        webhookProcessadoId, pedidoId, sw.ElapsedMilliseconds);
                    return ProcessarWebhookMpResultado.Aprovado;

                case "rejected":
                case "cancelled":
                case "refunded":
                case "charged_back":
                    await AplicarRecusadoAsync(webhook, pedido, detalhes.StatusDetail, ct);
                    logger.LogInformation(
                        "processar_webhook_mp_recusado webhookId={WebhookId} pedidoId={PedidoId} motivo={Motivo} elapsedMs={Ms}",
                        webhookProcessadoId, pedidoId, detalhes.StatusDetail, sw.ElapsedMilliseconds);
                    return ProcessarWebhookMpResultado.Recusado;

                case "pending":
                case "in_process":
                case "authorized":
                    // Mantém webhook em Received pra próxima passada do worker — re-check em ~60 s.
                    logger.LogInformation(
                        "processar_webhook_mp_pendente webhookId={WebhookId} pedidoId={PedidoId} status={Status}",
                        webhookProcessadoId, pedidoId, statusNormalizado);
                    return ProcessarWebhookMpResultado.Pendente;

                default:
                    logger.LogWarning(
                        "processar_webhook_mp_status_desconhecido webhookId={WebhookId} status={Status}",
                        webhookProcessadoId, detalhes.Status);
                    webhook.MarcarErro($"Status MP desconhecido: '{detalhes.Status}'");
                    await webhookRepository.UpdateAsync(webhook, ct);
                    return ProcessarWebhookMpResultado.Erro;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "processar_webhook_mp_erro_aplicacao webhookId={WebhookId} pedidoId={PedidoId}",
                webhookProcessadoId, pedidoId);
            try { webhook.MarcarErro($"Aplicação falhou: {ex.GetType().Name}: {ex.Message}"); }
            catch { /* já em status terminal */ }
            await webhookRepository.UpdateAsync(webhook, ct);
            return ProcessarWebhookMpResultado.Erro;
        }
    }

    private async Task AplicarAprovadoAsync(
        WebhookProcessado webhook, Domain.Entities.Pedido pedido, CancellationToken ct)
    {
        // Idempotência: se já aprovado/aguardando aprovação babá ou estado avançado, não regredir.
        var statusAtual = pedido.StatusEnum;
        if (statusAtual is StatusPedido.AguardandoAprovacaoBaba
                       or StatusPedido.Aguardando
                       or StatusPedido.Preparando
                       or StatusPedido.Pronto
                       or StatusPedido.Entregue)
        {
            // Reentrante — marca webhook processed sem mudar pedido.
            webhook.MarcarProcessado(pedido.EmpresaId);
            await webhookRepository.UpdateAsync(webhook, ct);
            return;
        }

        pedido.Status = StatusPedidoMapper.AguardandoAprovacaoBaba;
        pedido.AlteradoEm = DateTime.UtcNow;
        await pedidoRepository.UpdateAsync(pedido, ct);

        // Outbox/publish — handler downstream notifica WhatsApp da babá.
        await publicadorEventos.PublicarAsync(new NotificarBabaPedidoNovoEvent(
            PedidoId: pedido.Id,
            EmpresaId: pedido.EmpresaId,
            StorefrontId: Guid.Empty)); // resolver via Pedido.StorefrontId em iteração futura

        webhook.MarcarProcessado(pedido.EmpresaId);
        await webhookRepository.UpdateAsync(webhook, ct);
    }

    private async Task AplicarRecusadoAsync(
        WebhookProcessado webhook, Domain.Entities.Pedido pedido, string? statusDetail, CancellationToken ct)
    {
        // Idempotência: cancelado é estado terminal.
        if (pedido.StatusEnum == StatusPedido.Cancelado)
        {
            webhook.MarcarProcessado(pedido.EmpresaId);
            await webhookRepository.UpdateAsync(webhook, ct);
            return;
        }

        var motivo = string.IsNullOrWhiteSpace(statusDetail)
            ? "Pagamento recusado pelo MercadoPago"
            : $"Pagamento recusado: {statusDetail}";

        pedido.Status = StatusPedidoMapper.Cancelado;
        pedido.CanceladoEm = DateTime.UtcNow;
        pedido.AlteradoEm = DateTime.UtcNow;
        await pedidoRepository.UpdateAsync(pedido, ct);

        // Libera vaga (idempotente). Handler também publica PedidoCanceladoEvent.
        await publicadorEventos.PublicarAsync(new PedidoCanceladoEvent(
            PedidoId: pedido.Id,
            StorefrontId: Guid.Empty,
            Motivo: motivo));

        // Libera vaga diretamente também (defesa em profundidade — handler é Outbox-driven).
        await vagaOcupadaRepository.LiberarPorPedidoAsync(pedido.Id, motivo, ct);

        // Notifica cliente.
        await publicadorEventos.PublicarAsync(new NotificarClientePagamentoRecusadoEvent(
            PedidoId: pedido.Id,
            EmpresaId: pedido.EmpresaId,
            ClienteId: pedido.ClienteId ?? Guid.Empty,
            MotivoRecusa: motivo));

        webhook.MarcarProcessado(pedido.EmpresaId);
        await webhookRepository.UpdateAsync(webhook, ct);
    }
}
