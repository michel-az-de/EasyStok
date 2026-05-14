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
    SessionService session) : BaseController(session)
{
    [HttpGet("/dispositivos")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Dispositivos";
        ViewBag.ActiveMenuItem = "Dispositivos";

        var listResult = await svc.ListarAsync();
        if (HasError(listResult)) return View(new List<MobileDeviceApi>());

        ApiResult<List<DeviceHealthApi>> healthResult;
        try { healthResult = await opSvc.ObterSaudeDevicesAsync(); }
        catch { healthResult = ApiResult<List<DeviceHealthApi>>.Fail("ERR", "Falha ao carregar saúde dos dispositivos."); }

        // Mapeia health por id pra view consultar O(1).
        var healthById = healthResult.Success && healthResult.Data != null
            ? healthResult.Data.ToDictionary(h => h.Id)
            : new Dictionary<string, DeviceHealthApi>();
        ViewBag.HealthById = healthById;

        return View(listResult.Data ?? []);
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
