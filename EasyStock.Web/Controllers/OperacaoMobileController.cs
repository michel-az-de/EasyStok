using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Painel /operacao — dashboard ao vivo da operação mobile (Onda 4).
/// </summary>
public class OperacaoMobileController(
    OperacaoMobileService svc,
    MobileDevicesService devicesSvc,
    SessionService session) : BaseController(session)
{
    [HttpGet("/operacao")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Operação Mobile";
        ViewBag.ActiveMenuItem = "Operacao";

        var dashTask = svc.ObterDashboardAsync();
        var devicesTask = devicesSvc.ListarAsync();
        await Task.WhenAll(dashTask, devicesTask);

        var dashResult = await dashTask;
        var devicesResult = await devicesTask;

        var dash = dashResult.Success ? dashResult.Data : null;
        var devices = devicesResult.Success ? devicesResult.Data ?? [] : [];

        ViewBag.Devices = devices;
        return View(dash);
    }

    [HttpPost("/operacao/devices/{id}/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnfileirarComando(string id, string commandType, string? payloadJson)
    {
        var result = await svc.EnfileirarComandoAsync(id, commandType, payloadJson);
        Toast(result.Success ? "success" : "error",
            result.Success
                ? $"Comando '{commandType}' enviado pro dispositivo."
                : (result.ErrorMessage ?? "Falha ao enviar comando."));
        return RedirectToAction(nameof(Index));
    }
}
