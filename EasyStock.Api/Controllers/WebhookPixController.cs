using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookPixController(
    ICobrancaAssinaturaRepository cobrancaRepo,
    IAssinaturaEmpresaRepository assinaturaRepo,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    RegistrarPagamentoFaturaUseCase registrarPagamentoFaturaUseCase,
    ReconciliarPixParcelaReceberUseCase reconciliarPixParcelaReceberUseCase,
    ILogger<WebhookPixController> logger,
    IWebHostEnvironment env) : ControllerBase
{
    [HttpPost("pix")]
    [AllowAnonymous]
    public async Task<IActionResult> Pix()
    {
        // Lê body bruto pra calcular HMAC e desserializar.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (!ValidarAssinatura(rawBody))
        {
            logger.LogWarning("Webhook Pix: assinatura HMAC inválida ou ausente. Recusando.");
            return Unauthorized();
        }

        JsonElement payload;
        try { payload = JsonDocument.Parse(rawBody).RootElement; }
        catch (JsonException jx)
        {
            logger.LogWarning(jx, "Webhook Pix: payload JSON invalido. Recusando.");
            return BadRequest(new { error = "INVALID_JSON" });
        }

        try
        {
            if (!payload.TryGetProperty("pix", out var pixArray) || pixArray.ValueKind != JsonValueKind.Array)
                return Ok();

            foreach (var item in pixArray.EnumerateArray())
            {
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

                await ProcessarPagamentoAsync(txid, valorPago);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar webhook Pix");
            return StatusCode(500);
        }

        return Ok();
    }

    private bool ValidarAssinatura(string body)
    {
        var secret = configuration["Efi:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Bool tipado: "true" string ou bool true caem em true; qualquer outra
            // coisa = false. Evita interpretacao frouxa de string.
            var allowUnsigned = configuration.GetValue<bool>("Efi:WebhookAllowUnsigned", false);

            // Fail-secure: o escape hatch e so DEV/sandbox. Em Production, NUNCA
            // aceitar webhook nao-assinado mesmo com a flag ligada por engano.
            if (allowUnsigned && env.IsProduction())
            {
                logger.LogError(
                    "Webhook Pix: WebhookAllowUnsigned=true IGNORADO em Production — " +
                    "webhook nao-assinado recusado. Configure Efi:WebhookSecret.");
                return false;
            }

            if (allowUnsigned)
            {
                logger.LogWarning(
                    "Webhook Pix: secret ausente E WebhookAllowUnsigned=true — aceitando sem assinatura. " +
                    "NAO usar essa combinacao em Production.");
            }
            return allowUnsigned;
        }

        var headerSig = Request.Headers["X-Efi-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerSig)) return false;

        // Replay protection: header X-Efi-Timestamp em ms unix; janela ±5min.
        // Fail-secure: timestamp ausente ou invalido = recusa (antes pulava o
        // check, abrindo brecha de replay sem o header).
        var tsHeader = Request.Headers["X-Efi-Timestamp"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tsHeader) || !long.TryParse(tsHeader, out var ts))
        {
            logger.LogWarning("Webhook Pix: X-Efi-Timestamp ausente ou invalido. Recusando.");
            return false;
        }

        var diff = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts);
        if (diff > 5 * 60 * 1000)
        {
            logger.LogWarning("Webhook Pix: timestamp fora da janela ({Diff}ms). Recusando.", diff);
            return false;
        }

        // Timestamp prefixa o body assinado — defesa contra replay com novo ts.
        var toSign = $"{tsHeader}.{body}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(headerSig.Trim().ToLowerInvariant()));
    }

    private async Task ProcessarPagamentoAsync(string txid, decimal? valorPago)
    {
        // Roteamento por prefixo de txid:
        // - "cr..." -> parcela ContaReceber (CAP/CAR module)
        // - demais  -> CobrancaAssinatura (faturamento SaaS)
        if (txid.StartsWith("cr", StringComparison.OrdinalIgnoreCase))
        {
            var r = await reconciliarPixParcelaReceberUseCase.ExecuteAsync(
                new ReconciliarPixParcelaReceberCommand(txid, valorPago, DateTime.UtcNow));
            if (r.Reconciliado)
                logger.LogInformation("Webhook Pix: parcela CR reconciliada (txid={Txid} parcela={ParcelaId} conta={ContaId})",
                    txid, r.ParcelaId, r.ContaId);
            else
                logger.LogWarning("Webhook Pix: parcela CR nao reconciliada (txid={Txid} motivo={Motivo})",
                    txid, r.Motivo);
            return;
        }

        // Lock pessimista em "Txid" via SELECT FOR UPDATE serializa duplo-fire do
        // Efi (ate 5 retentativas em 5 min) — sem isso, dois webhooks simultaneos
        // passam o check Pendente e renovam a assinatura em duplicidade. O bloco
        // inteiro roda dentro de IExecutionStrategy pra ser compativel com
        // EnableRetryOnFailure (Npgsql).
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var cobranca = await cobrancaRepo.GetByTxidComLockAsync(txid, ct);
            if (cobranca is null)
            {
                logger.LogWarning("Webhook Pix: cobrança não encontrada para txid {Txid}", txid);
                return;
            }

            if (cobranca.Status != Domain.Enums.StatusCobranca.Pendente)
            {
                logger.LogDebug("Webhook Pix: cobrança {Txid} já processada (status {Status})", txid, cobranca.Status);
                return;
            }

            // Validação de valor: o pago precisa ser >= esperado (tolerância 1 centavo
            // pra arredondamento). Subpagamento não ativa plano. Webhook sem campo
            // valor é recusado pra evitar bypass.
            if (valorPago is null)
            {
                logger.LogWarning("Webhook Pix: txid {Txid} sem campo valor — recusando.", txid);
                return;
            }

            const decimal toleranciaCentavo = 0.01m;
            var diferenca = decimal.Round(valorPago.Value - cobranca.Valor, 2);

            if (diferenca < -toleranciaCentavo)
            {
                logger.LogWarning(
                    "Webhook Pix: valor pago ({Pago}) menor que esperado ({Esperado}) para txid {Txid}. Recusando.",
                    valorPago.Value, cobranca.Valor, txid);
                return;
            }

            if (diferenca > toleranciaCentavo)
            {
                // Superpagamento detectado: confirma renovacao mas grita no log
                // pra reconciliacao manual (eventual reembolso/ajuste contabil).
                // NAO bloqueia ativacao do plano — cliente pagou de fato.
                logger.LogWarning(
                    "Webhook Pix: SUPERPAGAMENTO em txid {Txid}. Pago={Pago} Esperado={Esperado} Diferenca={Diferenca}. " +
                    "Renovando assinatura, mas registrar reconciliacao manual.",
                    txid, valorPago.Value, cobranca.Valor, diferenca);
            }

            cobranca.MarcarComoPaga();
            await cobrancaRepo.UpdateAsync(cobranca);

            // F5 — Convivencia: se a cobranca tem FaturaId linkada (gerada apos F5),
            // registra FaturaPagamento confirmado. Idempotencia: se chamado duas vezes
            // (webhook duplicado mas que escapa do filtro de status acima — improvavel),
            // o UseCase chama Fatura.RegistrarPagamento que adiciona um pagamento novo,
            // o que recalcularia status para Paga (idempotente quando ja Paga). Erros
            // aqui NAO bloqueiam a renovacao SaaS — apenas log warning.
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
                        DadosGatewayJson: System.Text.Json.JsonSerializer.Serialize(new { txid, valorPago = valorPago.Value }),
                        StatusInicial: StatusFaturaPagamento.Confirmado,
                        Observacao: "Confirmado via webhook Efi Pix",
                        OrigemRegistro: "webhook-pix"
                    ));
                    logger.LogInformation(
                        "Pagamento Pix registrado na Fatura. FaturaId={FaturaId} Txid={Txid} Valor={Valor}",
                        cobranca.FaturaId, txid, valorPago.Value);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Falha ao registrar pagamento Pix na Fatura {FaturaId} (cobranca {Txid}). " +
                        "SaaS renova vigencia normalmente, mas Fatura ficara sem o pagamento ate reconciliacao.",
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
                    // Reativar lança se status for Cancelada — nesse caso
                    // criamos nova vigência sem chamar Reativar.
                    if (assinatura.Status == Domain.Enums.StatusAssinatura.Suspensa ||
                        assinatura.Status == Domain.Enums.StatusAssinatura.Expirada)
                    {
                        assinatura.Reativar();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Não foi possível reativar assinatura {Id}; renovando vigência apenas.", assinatura.Id);
                }

                // ACUMULA vigência: se DataFim ainda no futuro, soma a partir dela
                // (evita perder dias quando paga adiantado). Senão, soma a partir de hoje.
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
        });
    }
}
