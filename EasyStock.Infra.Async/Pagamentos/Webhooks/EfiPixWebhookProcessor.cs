using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos.Webhooks;

/// <summary>
/// Processa o payload de webhooks Pix da Efi para o <c>WebhookGatewayController</c>
/// generico. Espelha <c>WebhookPixController.ProcessarPagamentoAsync</c>.
///
/// <para>
/// Formato Efi: <c>{ pix: [{ txid, valor, ... }, ...] }</c>. Pode trazer
/// multiplas confirmacoes em uma chamada — cada uma processada individualmente.
/// Falhas em items individuais sao logadas mas nao abortam o batch.
/// </para>
///
/// <para>
/// Roteamento por prefixo de txid: <c>cr...</c> da baixa em parcela de conta a
/// receber. Qualquer outro txid (inclusive os da cobranca de assinatura SaaS,
/// removida na poda P02) e desconhecido: o resultado volta com
/// <c>Erro = "txid_desconhecido"</c> e o controller responde 200.
/// </para>
/// </summary>
public sealed class EfiPixWebhookProcessor(
    ReconciliarPixParcelaReceberUseCase reconciliarPixParcelaReceberUseCase,
    ILogger<EfiPixWebhookProcessor> logger) : IGatewayWebhookProcessor
{
    public const string ErroTxidDesconhecido = "txid_desconhecido";

    public string Provedor => "EfiPix";

    public async Task<ResultadoWebhookGateway> ProcessarAsync(string rawBody, IDictionary<string, string?> headers, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return ResultadoWebhookGateway.Ok;

        JsonElement payload;
        try
        {
            // Clone libera o JsonDocument (e o ArrayPool interno) mantendo o payload acessivel.
            using var doc = JsonDocument.Parse(rawBody);
            payload = doc.RootElement.Clone();
        }
        catch (JsonException jx)
        {
            logger.LogWarning(jx, "Webhook Pix: payload JSON invalido. Ignorando.");
            return ResultadoWebhookGateway.Ok;
        }

        if (!payload.TryGetProperty("pix", out var pixArray) || pixArray.ValueKind != JsonValueKind.Array)
            return ResultadoWebhookGateway.Ok;

        var algumDesconhecido = false;
        Exception? falha = null;
        foreach (var item in pixArray.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var txid = item.TryGetProperty("txid", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(txid)) continue;

            if (!txid.StartsWith("cr", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Webhook Pix: txid {Txid} desconhecido. Ignorando.", txid);
                algumDesconhecido = true;
                continue;
            }

            decimal? valorPago = null;
            if (item.TryGetProperty("valor", out var v))
            {
                var raw = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                if (decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    valorPago = parsed;
            }

            try
            {
                var r = await reconciliarPixParcelaReceberUseCase.ExecuteAsync(
                    new ReconciliarPixParcelaReceberCommand(txid, valorPago, DateTime.UtcNow), ct);
                if (!r.Reconciliado)
                    logger.LogWarning("Webhook Pix: parcela CR nao reconciliada (txid={Txid} motivo={Motivo})",
                        txid, r.Motivo);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EfiPixWebhookProcessor: falha ao processar txid {Txid}", txid);
                if (ex is OperationCanceledException) throw;
                falha ??= ex;
            }
        }

        // Propaga para o controller responder 500: a Efi so reenvia se nao receber 200 (#787);
        // engolir aqui marcava o webhook como Sucesso e o Pix nunca baixava.
        if (falha is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(falha).Throw();

        return algumDesconhecido
            ? ResultadoWebhookGateway.Falha(ErroTxidDesconhecido)
            : ResultadoWebhookGateway.Ok;
    }
}
