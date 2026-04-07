using EasyStock.Web.Models.ViewModels.Fornecedores;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class FornecedoresController(FornecedoresService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/fornecedores")]
    public async Task<IActionResult> Index(string? search = null, string? status = null)
    {
        ViewBag.Title = "Fornecedores";
        ViewBag.ActiveMenuItem = "Fornecedores";

        var result = await svc.ListarAsync(status, search);
        var vm = new FornecedoresListViewModel
        {
            Search = search,
            FiltroStatus = status
        };

        if (result.Success)
        {
            vm.Items = result.Data!;
            vm.TotalPedidosAbertos = 0; // loaded separately via badge
        }

        return View(vm);
    }

    [HttpGet("/fornecedores/pedidos")]
    public async Task<IActionResult> PedidosAbertos()
    {
        ViewBag.Title = "Pedidos em Aberto";
        ViewBag.ActiveMenuItem = "Fornecedores";

        var result = await svc.ListarPedidosAbertosAsync();
        var vm = new PedidosAbertosViewModel();

        if (result.Success)
            vm.Pedidos = result.Data!;

        return View(vm);
    }

    [HttpGet("/fornecedores/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Fornecedor";
        ViewBag.ActiveMenuItem = "Fornecedores";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var pedidosResult = await svc.ListarPedidosAbertosAsync();
        var vm = new FornecedorDetailViewModel
        {
            Fornecedor = result.Data!,
            PedidosAbertos = pedidosResult.Success
                ? pedidosResult.Data!.Where(p => p.FornId == id).ToList()
                : []
        };

        return View(vm);
    }

    [HttpPost("/fornecedores")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(
        string nome, string? cnpj, string? resp, string? email, string? tel,
        int lead, string pgto, string tipo, string cats, string? site,
        string? min, string? frete, string? obs)
    {
        var result = await svc.CriarAsync(new
        {
            nome, cnpj, resp, email, tel, lead, pgto, tipo, cats, site, min, frete, obs
        });

        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Fornecedor criado com sucesso!");
        return RedirectToAction(nameof(Detail), new { id = result.Data!.Id });
    }

    [HttpPost("/fornecedores/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(string id,
        string nome, string? cnpj, string? resp, string? email, string? tel,
        int lead, string pgto, string tipo, string cats, string? site,
        string? min, string? frete, string? obs)
    {
        var result = await svc.EditarAsync(id, new
        {
            nome, cnpj, resp, email, tel, lead, pgto, tipo, cats, site, min, frete, obs
        });

        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Fornecedor atualizado com sucesso!");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/fornecedores/{id}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(string id)
    {
        var result = await svc.ExcluirAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Fornecedor excluído com sucesso!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/fornecedores/{fornId}/pedidos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarPedido(string fornId,
        string? obs, DateOnly? dtPrevista, List<string> produtoIds,
        List<int> qtys, List<decimal> custos)
    {
        var itens = produtoIds.Select((pid, i) => new
        {
            produtoId = pid,
            qty = i < qtys.Count ? qtys[i] : 0,
            custo = i < custos.Count ? custos[i] : 0m
        }).ToList();

        var result = await svc.CriarPedidoAsync(fornId, new { obs, dtPrevista, itens });
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id = fornId });

        Toast("success", "Pedido criado com sucesso!");
        return RedirectToAction(nameof(PedidosAbertos));
    }

    [HttpPost("/fornecedores/{fornId}/pedidos/{pedId}/receber")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReceberPedido(string fornId, string pedId, string? obs)
    {
        var result = await svc.ReceberPedidoAsync(fornId, pedId, new { obs });
        if (HasError(result)) return RedirectToAction(nameof(PedidosAbertos));

        Toast("success", "Pedido recebido com sucesso!");
        return RedirectToAction(nameof(PedidosAbertos));
    }

    [HttpPost("/fornecedores/{fornId}/pedidos/{pedId}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarPedido(string fornId, string pedId)
    {
        var result = await svc.CancelarPedidoAsync(fornId, pedId);
        if (HasError(result)) return RedirectToAction(nameof(PedidosAbertos));

        Toast("success", "Pedido cancelado.");
        return RedirectToAction(nameof(PedidosAbertos));
    }
}
