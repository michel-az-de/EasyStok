using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos.Webhooks;

/// <summary>
/// Processa o webhook de pagamentos do Mercado Pago (S32) para o <c>WebhookGatewayController</c>
/// (<c>POST /api/webhooks/mercadopago</c>). Espelha o <see cref="EfiPixWebhookProcessor"/>.
///
/// <para>Fluxo:</para>
/// <list type="number">
///   <item>Só o tópico <c>payment</c> interessa; os demais (ex.: <c>merchant_order</c>) são ignorados com log.</item>
///   <item>O corpo só fornece <c>data.id</c>, que precisa ser numérico. <b>Nada mais do corpo é usado</b>: o
///     estado do pagamento vem de <c>GET v1/payments/{id}</c> (a assinatura prova a origem, não o conteúdo atual).</item>
///   <item><c>external_reference</c> = <c>PedidoId</c>. Outra referência (fatura SaaS, integração alheia) é ignorada.</item>
///   <item><c>approved</c> → <see cref="ConfirmarPagamentoPedidoUseCase"/> (S11): lock no pedido, valor contra a
///     cobrança, <c>AguardandoPagamento → Aguardando</c>, <c>PedidoPagamento</c> e <c>pedido.pago</c> no outbox.
///     Estorno, contestação e recusa → <see cref="AtualizarCobrancaPorPagamentoUseCase"/>.</item>
/// </list>
///
/// <para>
/// Idempotência em camadas: <c>WebhookRecebido</c> por id da notificação no controller; o mesmo pagamento
/// confirmado de novo é no-op no use case (cobrança já paga com esse id). Falha na consulta propaga: o
/// controller responde 500 e o Mercado Pago reenvia.
/// </para>
///
/// <para>
/// Pendente para S12: com <c>Pedido.RequerAprovacao</c>, destino <c>AguardandoAprovacaoBaba</c> (decidido no use case).
/// </para>
/// </summary>
public sealed class MercadoPagoWebhookProcessor(
    IMercadoPagoClient mercadoPagoClient,
    ConfirmarPagamentoPedidoUseCase confirmarPagamento,
    AtualizarCobrancaPorPagamentoUseCase atualizarCobranca,
    ILogger<MercadoPagoWebhookProcessor> logger) : IGatewayWebhookProcessor
{
    public const string TopicoPagamento = "payment";

    /// <summary>Bate com <see cref="MercadoPagoSignatureValidator.Provedor"/>.</summary>
    public string Provedor => "MercadoPago";

    public async Task ProcessarAsync(string rawBody, IDictionary<string, string?> headers, CancellationToken ct = default)
    {
        var pagamentoId = ExtrairPagamentoId(rawBody);
        if (pagamentoId is null) return;

        var pagamento = await mercadoPagoClient.ConsultarPagamentoAsync(pagamentoId, ct);
        if (pagamento is null)
        {
            logger.LogWarning("Webhook MercadoPago: pagamento {PagamentoId} desconhecido na consulta. Ignorando.", pagamentoId);
            return;
        }

        if (!Guid.TryParse(pagamento.ExternalReference, out var pedidoId))
        {
            logger.LogInformation("Webhook MercadoPago: pagamento {PagamentoId} sem pedido do EasyStok como referencia. Ignorando.", pagamentoId);
            return;
        }

        if (pagamento.Aprovado)
        {
            var r = await confirmarPagamento.ExecuteAsync(new ConfirmarPagamentoPedidoInput(
                pedidoId, pagamentoId, PagamentoMercadoPago.Approved, pagamento.TransactionAmount,
                pagamento.PaymentMethodId, pagamento.PaymentTypeId, pagamento.DateApproved), ct);
            logger.LogInformation("Webhook MercadoPago: pagamento {PagamentoId} pedido {PedidoId} confirmacao={Situacao}",
                pagamentoId, pedidoId, r.Situacao);
            return;
        }

        var situacao = await atualizarCobranca.ExecuteAsync(
            new AtualizarCobrancaPorPagamentoInput(pedidoId, pagamentoId, pagamento.Status), ct);
        logger.LogInformation("Webhook MercadoPago: pagamento {PagamentoId} pedido {PedidoId} nao aprovado atualizacao={Situacao}",
            pagamentoId, pedidoId, situacao);
    }

    /// <summary>
    /// <c>data.id</c> de uma notificação <c>payment</c>, normalizado a partir de um <see cref="long"/>: o texto que
    /// segue para a URL da consulta e para o log nunca é o do corpo (evita injeção de caminho e log forging).
    /// </summary>
    private string? ExtrairPagamentoId(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return null;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            logger.LogWarning("Webhook MercadoPago: payload JSON invalido. Ignorando.");
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var tipo)
            || tipo.ValueKind != JsonValueKind.String
            || !string.Equals(tipo.GetString(), TopicoPagamento, StringComparison.Ordinal))
        {
            logger.LogInformation("Webhook MercadoPago: topico diferente de payment. Ignorando.");
            return null;
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("id", out var idEl))
        {
            logger.LogWarning("Webhook MercadoPago: notificacao payment sem data.id. Ignorando.");
            return null;
        }

        var bruto = idEl.ValueKind switch
        {
            JsonValueKind.String => idEl.GetString(),
            JsonValueKind.Number => idEl.GetRawText(),
            _ => null,
        };
        if (!long.TryParse(bruto, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            logger.LogWarning("Webhook MercadoPago: data.id fora do formato numerico. Ignorando.");
            return null;
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }
}
