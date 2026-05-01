using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhookPixController(
    ICobrancaAssinaturaRepository cobrancaRepo,
    IAssinaturaEmpresaRepository assinaturaRepo,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<WebhookPixController> logger) : ControllerBase
{
    [HttpPost("pix")]
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
        catch { return BadRequest(); }

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
            return string.Equals(configuration["Efi:WebhookAllowUnsigned"], "true", StringComparison.OrdinalIgnoreCase);
        }

        var headerSig = Request.Headers["X-Efi-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerSig)) return false;

        // Replay protection: header X-Efi-Timestamp em ms unix; aceita janela ±5min.
        var tsHeader = Request.Headers["X-Efi-Timestamp"].FirstOrDefault();
        if (long.TryParse(tsHeader, out var ts))
        {
            var diff = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts);
            if (diff > 5 * 60 * 1000)
            {
                logger.LogWarning("Webhook Pix: timestamp fora da janela ({Diff}ms). Recusando.", diff);
                return false;
            }
        }

        // Inclui timestamp no payload assinado se presente (defesa contra replay
        // mesmo se atacante reenviar com timestamp atualizado).
        var toSign = string.IsNullOrWhiteSpace(tsHeader) ? body : $"{tsHeader}.{body}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(headerSig.Trim().ToLowerInvariant()));
    }

    private async Task ProcessarPagamentoAsync(string txid, decimal? valorPago)
    {
        var cobranca = await cobrancaRepo.GetByTxidAsync(txid);
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

        if (valorPago.Value + 0.01m < cobranca.Valor)
        {
            logger.LogWarning(
                "Webhook Pix: valor pago ({Pago}) menor que esperado ({Esperado}) para txid {Txid}. Recusando.",
                valorPago.Value, cobranca.Valor, txid);
            return;
        }

        cobranca.MarcarComoPaga();
        await cobrancaRepo.UpdateAsync(cobranca);

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
    }
}
