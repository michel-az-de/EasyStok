using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookPixController(
    IConfiguration configuration,
    ReconciliarPixParcelaReceberUseCase reconciliarPixParcelaReceberUseCase,
    ILogger<WebhookPixController> logger,
    IWebHostEnvironment env) : ControllerBase
{
    [HttpPost("pix")]
    [AllowAnonymous]
    public async Task<IActionResult> Pix(CancellationToken ct)
    {
        // Lê body bruto pra calcular HMAC e desserializar.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
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

                await ProcessarPagamentoAsync(txid, valorPago, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Efi desistiu da conexao: nao e erro do servidor; ela reenvia e a reconciliacao e idempotente.
            throw;
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

    private async Task ProcessarPagamentoAsync(string txid, decimal? valorPago, CancellationToken ct)
    {
        // Roteamento por prefixo de txid:
        // - "cr..." -> parcela ContaReceber (CAP/CAR module)
        // - demais  -> desconhecido. A cobranca de assinatura SaaS saiu na poda P02;
        //              txid antigo responde 200 (o Efi para de retentar) sem excecao.
        if (!txid.StartsWith("cr", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Webhook Pix: txid {Txid} desconhecido. Ignorando.", txid);
            return;
        }

        var r = await reconciliarPixParcelaReceberUseCase.ExecuteAsync(
            new ReconciliarPixParcelaReceberCommand(txid, valorPago, DateTime.UtcNow), ct);
        if (r.Reconciliado)
            logger.LogInformation("Webhook Pix: parcela CR reconciliada (txid={Txid} parcela={ParcelaId} conta={ContaId})",
                txid, r.ParcelaId, r.ContaId);
        else
            logger.LogWarning("Webhook Pix: parcela CR nao reconciliada (txid={Txid} motivo={Motivo})",
                txid, r.Motivo);
    }
}
