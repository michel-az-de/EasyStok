using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos.Webhooks;

/// <summary>
/// Processa o payload de webhooks Pix da Efi. Espelha
/// <c>WebhookPixController.ProcessarPagamentoAsync</c> para uso pelo
/// <c>WebhookGatewayController</c> generico.
///
/// <para>
/// Formato Efi: <c>{ pix: [{ txid, valor, ... }, ...] }</c>. Pode trazer
/// multiplas confirmacoes em uma chamada — cada uma processada individualmente.
/// Falhas em items individuais sao logadas mas nao abortam o batch.
/// </para>
///
/// <para>
/// Idempotencia em camadas:
/// </para>
/// <list type="number">
///   <item><c>WebhookGatewayController</c> registra eventId+hash em
///   <c>WebhookRecebido</c> (UNIQUE), barrando retentativas identicas.</item>
///   <item>Cada <c>txid</c> abre transacao explicita via
///   <see cref="IUnitOfWork.ExecuteInTransactionAsync(Func{CancellationToken, Task}, CancellationToken)"/>
///   e adquire lock pessimista com
///   <see cref="ICobrancaAssinaturaRepository.GetByTxidComLockAsync"/>
///   (<c>SELECT FOR UPDATE</c>). Serializa duplo-fire do Efi (ate 5
///   retentativas em 5 min) — sem isso, dois webhooks simultaneos
///   passariam o check <c>Pendente</c> e renovariam a assinatura em
///   duplicidade (+60d por 1 pagamento, ver bug #289).</item>
///   <item>Check <c>cobranca.Status != Pendente</c> dentro da transacao e
///   defesa secundaria — o lock impede que dois callers cheguem nele
///   simultaneamente com a mesma linha.</item>
/// </list>
/// </summary>
public sealed class EfiPixWebhookProcessor(
    ICobrancaAssinaturaRepository cobrancaRepo,
    IAssinaturaEmpresaRepository assinaturaRepo,
    IUnitOfWork unitOfWork,
    RegistrarPagamentoFaturaUseCase registrarPagamentoFaturaUseCase,
    IFalhaPagamentoNotifier falhaNotifier,
    ILogger<EfiPixWebhookProcessor> logger) : IGatewayWebhookProcessor
{
    public string Provedor => "EfiPix";

    public async Task ProcessarAsync(string rawBody, IDictionary<string, string?> headers, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return;

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
            return;
        }

        if (!payload.TryGetProperty("pix", out var pixArray) || pixArray.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in pixArray.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var txid = item.TryGetProperty("txid", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(txid)) continue;

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
                await ProcessarPagamentoAsync(txid, valorPago, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EfiPixWebhookProcessor: falha ao processar txid {Txid}", txid);
            }
        }
    }

    private async Task ProcessarPagamentoAsync(string txid, decimal? valorPago, CancellationToken ct)
    {
        // Bloco roda dentro de IExecutionStrategy (Npgsql EnableRetryOnFailure) e
        // adquire SELECT FOR UPDATE no Txid logo no inicio — serializa duplo-fire
        // do Efi e mantem idempotencia DB-side mesmo sob retry transitorio.
        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var cobranca = await cobrancaRepo.GetByTxidComLockAsync(txid, innerCt);
            if (cobranca is null)
            {
                logger.LogWarning("Webhook Pix: cobranca nao encontrada para txid {Txid}", txid);
                return;
            }

            if (cobranca.Status != StatusCobranca.Pendente)
            {
                logger.LogDebug("Webhook Pix: cobranca {Txid} ja processada (status {Status})", txid, cobranca.Status);
                return;
            }

            if (valorPago is null)
            {
                logger.LogWarning("Webhook Pix: txid {Txid} sem campo valor — recusando.", txid);
                await falhaNotifier.RegistrarFalhaAsync(
                    cobranca.EmpresaId, cobranca.FaturaId,
                    $"Webhook Pix txid={txid} chegou sem campo valor.", innerCt);
                return;
            }

            if (valorPago.Value + 0.01m < cobranca.Valor)
            {
                logger.LogWarning(
                    "Webhook Pix: valor pago ({Pago}) menor que esperado ({Esperado}) para txid {Txid}. Recusando.",
                    valorPago.Value, cobranca.Valor, txid);
                await falhaNotifier.RegistrarFalhaAsync(
                    cobranca.EmpresaId, cobranca.FaturaId,
                    $"Subpagamento Pix: pago {valorPago.Value:F2} < esperado {cobranca.Valor:F2} (txid {txid}).", innerCt);
                return;
            }

            cobranca.MarcarComoPaga();
            await cobrancaRepo.UpdateAsync(cobranca);

            // Convivencia F5 — registra FaturaPagamento confirmado quando ha link.
            if (cobranca.FaturaId.HasValue)
            {
                try
                {
                    await registrarPagamentoFaturaUseCase.ExecuteAsync(new RegistrarPagamentoFaturaCommand(
                        EmpresaId: cobranca.EmpresaId,
                        FaturaId: cobranca.FaturaId.Value,
                        Metodo: "pix",
                        Valor: valorPago.Value,
                        GatewayProvedor: "EfiPix",
                        GatewayTransactionId: txid,
                        DadosGatewayJson: JsonSerializer.Serialize(new { txid, valorPago = valorPago.Value }),
                        StatusInicial: StatusFaturaPagamento.Confirmado,
                        Observacao: "Confirmado via webhook Efi Pix",
                        OrigemRegistro: "webhook-pix"
                    ), innerCt);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "EfiPixWebhookProcessor: falha ao registrar pagamento na fatura {FaturaId} (txid {Txid}). " +
                        "SaaS renova vigencia normalmente; reconciliacao F6 deve fechar o gap.",
                        cobranca.FaturaId, txid);
                }
            }

            var assinatura = await assinaturaRepo.GetAtivaAsync(cobranca.EmpresaId)
                ?? (await assinaturaRepo.GetByEmpresaAsync(cobranca.EmpresaId))
                    .OrderByDescending(a => a.CriadoEm)
                    .FirstOrDefault();

            if (assinatura is not null)
            {
                try
                {
                    if (assinatura.Status == StatusAssinatura.Suspensa
                        || assinatura.Status == StatusAssinatura.Expirada)
                    {
                        assinatura.Reativar();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Nao foi possivel reativar assinatura {Id}; renovando vigencia apenas.", assinatura.Id);
                }

                // ACUMULA vigencia: se DataFim ainda no futuro, soma a partir dela
                // (evita perder dias quando paga adiantado). Senao, soma a partir de hoje.
                var now = DateTime.UtcNow;
                var baseDate = (assinatura.DataFim.HasValue && assinatura.DataFim.Value > now)
                    ? assinatura.DataFim.Value
                    : now;
                assinatura.DataFim = baseDate.AddDays(30);
                await assinaturaRepo.UpdateAsync(assinatura);
                logger.LogInformation("Assinatura renovada via Pix. EmpresaId: {EmpresaId}, Txid: {Txid}, novo DataFim: {Fim}",
                    cobranca.EmpresaId, txid, assinatura.DataFim);
            }

            await unitOfWork.CommitAsync();
        }, ct);
    }
}
