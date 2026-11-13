using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos.Webhooks;

/// <summary>
/// Processa o webhook de pagamentos do Mercado Pago (S32) para o <c>WebhookGatewayController</c>
/// (<c>POST /api/webhooks/mercadopago</c>). Espelha o <see cref="EfiPixWebhookProcessor"/>.
///
/// <para>Fluxo:</para>
/// <list type="number">
///   <item>Reconhece <c>payment</c> e <c>order.processed</c>; os demais (ex.: <c>merchant_order</c>) são ignorados com log.</item>
///   <item>Em <c>payment</c>, <c>data.id</c> deve ser numérico. Em <c>order.processed</c>, a referência
///     e o E2E localizam os ids exatos pela busca. Estado, valor e conta vêm de <c>GET v1/payments/{id}</c>.</item>
///   <item><c>external_reference</c> = <c>PedidoId</c>. Outra referência (fatura SaaS, integração alheia) é ignorada.</item>
///   <item><c>approved</c> → <see cref="ConfirmarPagamentoPedidoUseCase"/> (S11): lock no pedido, valor contra a
///     cobrança, <c>AguardandoPagamento → Aguardando</c>, <c>PedidoPagamento</c> e <c>pedido.pago</c> no outbox.
///     Estorno, contestação e recusa → <see cref="AtualizarCobrancaPorPagamentoUseCase"/>.</item>
/// </list>
///
/// <para>
/// Resultado (P02): desfecho de negócio processado (confirmado, repetido, valor menor, recusa, estorno) volta
/// <see cref="ResultadoWebhookGateway.Ok"/>; evento que não é de pedido do EasyStok volta
/// <see cref="ResultadoWebhookGateway.Falha"/> com o motivo, que o controller grava no <c>WebhookRecebido</c>
/// respondendo 200.
/// </para>
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
    ILogger<MercadoPagoWebhookProcessor> logger,
    IEstornosOnlineService estornosOnline,
    ICobrancaPedidoRepository cobrancas) : IGatewayWebhookProcessor
{
    public const string TopicoPagamento = "payment";

    /// <summary>Bate com <see cref="MercadoPagoSignatureValidator.Provedor"/>.</summary>
    public string Provedor => "MercadoPago";

    public async Task<ResultadoWebhookGateway> ProcessarAsync(string rawBody, IDictionary<string, string?> headers, CancellationToken ct = default)
    {
        if (ReferenciaDaOrdem(rawBody) is { } ordem)
            return await ProcessarOrdemAsync(ordem.Id, ordem.PedidoId, ct);

        var (pagamentoId, motivo) = ExtrairPagamentoId(rawBody);
        if (pagamentoId is null) return ResultadoWebhookGateway.Falha(motivo);
        return await ProcessarPagamentoAsync(pagamentoId, ct);
    }

    private async Task<ResultadoWebhookGateway> ProcessarOrdemAsync(string ordemId, Guid pedidoId, CancellationToken ct)
    {
        if (await cobrancas.ObterEmpresaIdDoPedidoAsync(pedidoId, ct) is null)
            return ResultadoWebhookGateway.Falha("pedido_sem_cobranca");

        // #2093: o E2E identifica o Pix exato, inclusive em cobrancas complementares.
        var encontrados = await mercadoPagoClient.BuscarPagamentosPixDaOrdemAsync(ordemId, ct);
        if (encontrados.Count == 0 || encontrados.Any(p =>
            !Guid.TryParse(p.ExternalReference, out var id) || id != pedidoId
            || p.PaymentMethodId != "pix" || string.IsNullOrWhiteSpace(p.TransactionId)))
            throw new InvalidOperationException("Mercado Pago ainda nao retornou os pagamentos Pix da ordem.");
        var resultado = ResultadoWebhookGateway.Ok;
        foreach (var pagamento in encontrados)
        {
            var atual = await ProcessarPagamentoAsync(pagamento.Id, ct, pagamento);
            if (!atual.Sucesso) resultado = atual;
        }
        return resultado;
    }

    private async Task<ResultadoWebhookGateway> ProcessarPagamentoAsync(string pagamentoId, CancellationToken ct,
        PagamentoMercadoPago? esperado = null)
    {
        var pagamento = await mercadoPagoClient.ConsultarPagamentoAsync(pagamentoId, ct);
        if (esperado is not null && (pagamento is null || !pagamento.Aprovado
            || !Guid.TryParse(pagamento.ExternalReference, out var referencia)
            || !Guid.TryParse(esperado.ExternalReference, out var referenciaEsperada) || referencia != referenciaEsperada
            || pagamento.PaymentMethodId != "pix" || string.IsNullOrWhiteSpace(pagamento.TransactionId)
            || !string.Equals(pagamento.TransactionId, esperado.TransactionId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Pagamento Pix da ordem ainda indisponivel ou divergente na reconsulta.");
        if (pagamento is null)
        {
            // I3.1 (invariante 7): 404 na reconsulta com cobrança online esperando pede reenvio (5xx).
            // Pode ser consulta com a credencial errada ou atraso do provedor; 200 perderia a baixa.
            if (await cobrancas.ExistePendenteOnlineAsync(ct))
            {
                logger.LogError("Webhook MercadoPago: pagamento {PagamentoId} desconhecido na consulta e ha cobranca pendente. Pedindo reenvio.", pagamentoId);
                throw new ReconsultaMercadoPagoSemRespostaException(pagamentoId);
            }

            logger.LogWarning("Webhook MercadoPago: pagamento {PagamentoId} desconhecido na consulta. Ignorando.", pagamentoId);
            return ResultadoWebhookGateway.Falha("pagamento_desconhecido");
        }

        if (!Guid.TryParse(pagamento.ExternalReference, out var pedidoId))
        {
            logger.LogInformation("Webhook MercadoPago: pagamento {PagamentoId} sem pedido do EasyStok como referencia. Ignorando.", pagamentoId);
            return ResultadoWebhookGateway.Falha("referencia_externa_desconhecida");
        }

        if (pagamento.Aprovado)
        {
            var r = await confirmarPagamento.ExecuteAsync(new ConfirmarPagamentoPedidoInput(
                pedidoId, pagamentoId, PagamentoMercadoPago.Approved, pagamento.TransactionAmount,
                pagamento.PaymentMethodId, pagamento.PaymentTypeId, pagamento.DateApproved,
                ContaRecebedoraId: pagamento.CollectorId), ct);
            logger.LogInformation("Webhook MercadoPago: pagamento {PagamentoId} pedido {PedidoId} confirmacao={Situacao}",
                pagamentoId, pedidoId, r.Situacao);
            if (pagamento.TransactionAmountRefunded > 0)
                await estornosOnline.SincronizarPagamentoAsync(pagamento, ct);
            return r.Situacao switch
            {
                SituacaoConfirmacaoPagamento.SemCobranca or SituacaoConfirmacaoPagamento.PedidoNaoEncontrado
                    => ResultadoWebhookGateway.Falha("pedido_sem_cobranca"),
                // I3.2: pagamento de outra conta apontando para pedido desta loja. Fica registrado, não confirma.
                SituacaoConfirmacaoPagamento.ContaDivergente => ResultadoWebhookGateway.Falha("conta_divergente"),
                _ => ResultadoWebhookGateway.Ok,
            };
        }

        if (pagamento.Estornado && await estornosOnline.SincronizarPagamentoAsync(pagamento, ct))
            return ResultadoWebhookGateway.Ok;

        var situacao = await atualizarCobranca.ExecuteAsync(
            new AtualizarCobrancaPorPagamentoInput(pedidoId, pagamentoId, pagamento.Status, pagamento.StatusDetail), ct);
        logger.LogInformation("Webhook MercadoPago: pagamento {PagamentoId} pedido {PedidoId} nao aprovado atualizacao={Situacao}",
            pagamentoId, pedidoId, situacao);
        return situacao is SituacaoAtualizacaoCobranca.SemCobranca or SituacaoAtualizacaoCobranca.PedidoNaoEncontrado
            ? ResultadoWebhookGateway.Falha("pedido_sem_cobranca")
            : ResultadoWebhookGateway.Ok;
    }

    /// <summary>Referencia de order.processed, usada apenas para pesquisar pagamentos na fonte.</summary>
    private static (string Id, Guid PedidoId)? ReferenciaDaOrdem(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("type", out var tipo) && tipo.ValueKind == JsonValueKind.String
                && tipo.GetString() == "order"
                && root.TryGetProperty("action", out var acao) && acao.ValueKind == JsonValueKind.String
                && acao.GetString() == "order.processed"
                && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("id", out var ordemId) && ordemId.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(ordemId.GetString())
                && data.TryGetProperty("transactions", out var transacoes) && transacoes.ValueKind == JsonValueKind.Object
                && transacoes.TryGetProperty("payments", out var pagamentos) && pagamentos.ValueKind == JsonValueKind.Array
                && pagamentos.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.Object
                    && p.TryGetProperty("payment_method", out var m)
                    && m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var nome)
                    && nome.ValueKind == JsonValueKind.String && nome.GetString() == "pix")
                && data.TryGetProperty("external_reference", out var referencia) && referencia.ValueKind == JsonValueKind.String
                && Guid.TryParse(referencia.GetString(), out var id) && id != Guid.Empty)
                return (ordemId.GetString()!, id);
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Id numerico de payment, fechado antes de compor a URL da consulta.</summary>
    private (string? Id, string Motivo) ExtrairPagamentoId(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return (null, "corpo_vazio");

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            logger.LogWarning("Webhook MercadoPago: payload JSON invalido. Ignorando.");
            return (null, "json_invalido");
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var tipo)
            || tipo.ValueKind != JsonValueKind.String
            || !string.Equals(tipo.GetString(), TopicoPagamento, StringComparison.Ordinal))
        {
            logger.LogInformation("Webhook MercadoPago: topico diferente de payment. Ignorando.");
            return (null, "topico_ignorado");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("id", out var idEl))
        {
            logger.LogWarning("Webhook MercadoPago: notificacao payment sem data.id. Ignorando.");
            return (null, "sem_data_id");
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
            return (null, "data_id_invalido");
        }

        return (id.ToString(CultureInfo.InvariantCulture), string.Empty);
    }
}
