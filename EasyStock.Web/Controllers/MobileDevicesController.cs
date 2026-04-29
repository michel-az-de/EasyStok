using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Painel /dispositivos — gestão de devices mobile pareados (Onda 1).
/// </summary>
public class MobileDevicesController(
    MobileDevicesService svc,
    SessionService session) : BaseController(session)
{
    [HttpGet("/dispositivos")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Dispositivos";
        ViewBag.ActiveMenuItem = "Dispositivos";

        var result = await svc.ListarAsync();
        if (HasError(result)) return View(new List<MobileDeviceApi>());
        return View(result.Data ?? []);
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
}
