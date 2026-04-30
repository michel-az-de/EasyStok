using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhookPixController(
    ICobrancaAssinaturaRepository cobrancaRepo,
    IAssinaturaEmpresaRepository assinaturaRepo,
    IUnitOfWork unitOfWork,
    ILogger<WebhookPixController> logger) : ControllerBase
{
    [HttpPost("pix")]
    public async Task<IActionResult> Pix([FromBody] JsonElement payload)
    {
        try
        {
            if (!payload.TryGetProperty("pix", out var pixArray) || pixArray.ValueKind != JsonValueKind.Array)
                return Ok();

            foreach (var item in pixArray.EnumerateArray())
            {
                var txid = item.TryGetProperty("txid", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(txid)) continue;

                await ProcessarPagamentoAsync(txid);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar webhook Pix");
            return StatusCode(500);
        }

        return Ok();
    }

    private async Task ProcessarPagamentoAsync(string txid)
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

        cobranca.MarcarComoPaga();
        await cobrancaRepo.UpdateAsync(cobranca);

        var assinatura = await assinaturaRepo.GetAtivaAsync(cobranca.EmpresaId)
            ?? (await assinaturaRepo.GetByEmpresaAsync(cobranca.EmpresaId))
                .OrderByDescending(a => a.CriadoEm)
                .FirstOrDefault();

        if (assinatura is not null)
        {
            assinatura.Reativar();
            assinatura.DataFim = DateTime.UtcNow.AddDays(30);
            await assinaturaRepo.UpdateAsync(assinatura);
            logger.LogInformation("Assinatura renovada via Pix. EmpresaId: {EmpresaId}, Txid: {Txid}", cobranca.EmpresaId, txid);
        }

        await unitOfWork.CommitAsync();
    }
}
