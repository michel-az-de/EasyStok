using EasyStock.Web.Models.Api;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Painel /dispositivos — gestão de devices mobile pareados (Onda 1).
/// </summary>
public class MobileDevicesController(
    MobileDevicesService svc,
    OperacaoMobileService opSvc,
    SessionService session,
    ILogger<MobileDevicesController> log) : BaseController(session)
{
    [HttpGet("/dispositivos")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Dispositivos";
        ViewBag.ActiveMenuItem = "Dispositivos";
        // Garantir HealthById SEMPRE definido — view crashava antes do fallback
        // se o controller retornasse cedo via HasError sem setar o ViewBag.
        ViewBag.HealthById = new Dictionary<string, DeviceHealthApi>();

        try
        {
            var listResult = await svc.ListarAsync();
            if (!listResult.Success)
            {
                log.LogWarning("MobileDevices.Listar falhou: {Code} {Message} (HTTP {Http} CID {Cid})",
                    listResult.ErrorCode, listResult.ErrorMessage, listResult.HttpStatus, listResult.CorrelationId);
                if (HasError(listResult)) return View(new List<MobileDeviceApi>());
            }

            ApiResult<List<DeviceHealthApi>> healthResult;
            try { healthResult = await opSvc.ObterSaudeDevicesAsync(); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "ObterSaudeDevices lançou exceção");
                healthResult = ApiResult<List<DeviceHealthApi>>.Fail("ERR", "Falha ao carregar saúde dos dispositivos.");
            }

            if (healthResult.Success && healthResult.Data != null)
                ViewBag.HealthById = healthResult.Data.ToDictionary(h => h.Id);

            return View(listResult.Data ?? []);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha inesperada carregando /dispositivos");
            Toast("warning", "Não foi possível carregar a lista de dispositivos agora. Tente novamente em instantes.");
            return View(new List<MobileDeviceApi>());
        }
    }

    [HttpPost("/dispositivos/parear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GerarCodigo(string? label, string? defaultOperatorName)
    {
        var result = await svc.GerarCodigoAsync(label, defaultOperatorName);
        if (HasError(result) || result.Data == null)
        {
            return RedirectToAction(nameof(Index));
        }

        // Repassa código pra view via TempData — mostra modal "use esse código no app".
        TempData["PairingCode"] = result.Data.PairingCode;
        TempData["PairingExpiresAt"] = result.Data.ExpiresAt.ToString("o");
        Toast("success", $"Código gerado: {result.Data.PairingCode} (válido por 10 min)");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/dispositivos/{id}/revogar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revogar(string id)
    {
        var result = await svc.RevogarAsync(id);
        if (!result.Success)
        {
            Toast("error", result.ErrorMessage ?? "Falha ao revogar dispositivo.");
        }
        else
        {
            Toast("success", "Dispositivo revogado.");
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/dispositivos/{id}/backups")]
    public async Task<IActionResult> Backups(string id)
    {
        ViewBag.Title = "Backups";
        ViewBag.ActiveMenuItem = "Dispositivos";
        ViewBag.DeviceId = id;

        var result = await svc.ListarBackupsAsync(id);
        if (HasError(result)) return View(new List<DeviceBackupSummaryApi>());
        return View(result.Data ?? []);
    }
}
