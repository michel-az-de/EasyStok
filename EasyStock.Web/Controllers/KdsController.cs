using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Kds;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// KDS (Kitchen Display System): tela operacional de cozinha/produção.
/// Mostra pedidos com status "aguardando" e "preparando" como cards grandes,
/// auto-refresh, ações rápidas (iniciar preparo / marcar pronto).
///
/// Reusa PedidosService — não há um endpoint específico no backend (ainda).
/// O fluxo de status é o mesmo de Pedidos (aguardando → preparando → pronto → entregue).
/// </summary>
public class KdsController(PedidosService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/kds")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "KDS — Cozinha";
        ViewBag.ActiveMenuItem = "Kds";

        var vm = new KdsViewModel();

        // Carrega pedidos abertos (aguardando + preparando + pronto).
        // Como o filtro é por status único na API, fazemos 3 chamadas paralelas.
        var aguardandoTask = svc.ListarAsync(status: "aguardando");
        var preparandoTask = svc.ListarAsync(status: "preparando");
        var prontoTask     = svc.ListarAsync(status: "pronto");
        await Task.WhenAll(aguardandoTask, preparandoTask, prontoTask);

        var aguardando = await aguardandoTask;
        var preparando = await preparandoTask;
        var pronto     = await prontoTask;

        vm.Aguardando = (aguardando.Success ? aguardando.Data : null) ?? new List<Pedido>();
        vm.Preparando = (preparando.Success ? preparando.Data : null) ?? new List<Pedido>();
        vm.Pronto     = (pronto.Success     ? pronto.Data     : null) ?? new List<Pedido>();

        // Ordenação: mais antigo primeiro (FIFO da cozinha).
        vm.Aguardando = vm.Aguardando.OrderBy(p => p.CriadoEm).ToList();
        vm.Preparando = vm.Preparando.OrderBy(p => p.CriadoEm).ToList();
        vm.Pronto     = vm.Pronto.OrderBy(p => p.CriadoEm).ToList();

        return View(vm);
    }

    /// <summary>Endpoint JSON para auto-refresh AJAX (sem reload completo).</summary>
    [HttpGet("/kds/json")]
    public async Task<IActionResult> Json()
    {
        var aguardandoTask = svc.ListarAsync(status: "aguardando");
        var preparandoTask = svc.ListarAsync(status: "preparando");
        var prontoTask     = svc.ListarAsync(status: "pronto");
        await Task.WhenAll(aguardandoTask, preparandoTask, prontoTask);

        var aguardando = await aguardandoTask;
        var preparando = await preparandoTask;
        var pronto     = await prontoTask;

        return Ok(new
        {
            aguardando = (aguardando.Success ? aguardando.Data : null) ?? new List<Pedido>(),
            preparando = (preparando.Success ? preparando.Data : null) ?? new List<Pedido>(),
            pronto     = (pronto.Success     ? pronto.Data     : null) ?? new List<Pedido>(),
            servidoEm  = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Ação rápida: avança o status do pedido.</summary>
    [HttpPost("/kds/{id}/avancar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Avancar(string id, string status)
    {
        var result = await svc.AtualizarStatusAsync(id, status);
        if (!result.Success)
            return BadRequest(new { success = false, errorMessage = result.ErrorMessage ?? "Erro ao atualizar pedido." });
        return Ok(new { success = true });
    }
}
