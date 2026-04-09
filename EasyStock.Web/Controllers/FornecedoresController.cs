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
            vm.Items = result.Data!;

        return View(vm);
    }

    [HttpGet("/fornecedores/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Fornecedor";
        ViewBag.ActiveMenuItem = "Fornecedores";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var vm = new FornecedorDetailViewModel { Fornecedor = result.Data! };

        var historicoResult = await svc.ObterHistoricoAsync(id);
        if (historicoResult.Success)
            vm.Historico = historicoResult.Data!;

        var estatisticasResult = await svc.ObterEstatisticasAsync(id);
        if (estatisticasResult.Success)
        {
            var stats = estatisticasResult.Data!;
            vm.TotalGasto = stats.TotalGasto;
            vm.LeadRealMedio = stats.LeadTimeRealMedioDias;
            vm.QuantidadePedidos = stats.QuantidadePedidos;
        }

        return View(vm);
    }

    [HttpPost("/fornecedores")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(
        string nome, string? documento, string? contato, string? email, string? telefone,
        int? leadTimeEstimadoDias, string? tipo, string? categoria, string? siteUrl,
        string? pedidoMinimo, string? fretePadrao, string? observacoes)
    {
        var result = await svc.CriarAsync(
            nome, documento, contato, email, telefone,
            leadTimeEstimadoDias, tipo, categoria, siteUrl,
            pedidoMinimo, fretePadrao, observacoes);

        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Fornecedor criado com sucesso!");
        return RedirectToAction(nameof(Detail), new { id = result.Data!.Id });
    }

    [HttpPost("/fornecedores/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(string id,
        string nome, string? documento, string? contato, string? email, string? telefone,
        int? leadTimeEstimadoDias, string? tipo, string? categoria, string? siteUrl,
        string? pedidoMinimo, string? fretePadrao, string? observacoes)
    {
        var result = await svc.EditarAsync(id,
            nome, documento, contato, email, telefone,
            leadTimeEstimadoDias, tipo, categoria, siteUrl,
            pedidoMinimo, fretePadrao, observacoes);

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

        Toast("success", "Fornecedor desativado com sucesso!");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/fornecedores/pedidos-abertos")]
    public async Task<IActionResult> PedidosAbertos()
    {
        ViewBag.Title = "Pedidos em Aberto";
        ViewBag.ActiveMenuItem = "Fornecedores";

        var vm = new PedidosAbertosViewModel();
        var result = await svc.ListarPedidosAbertosAsync();
        if (result.Success)
            vm.Pedidos = result.Data!;

        return View(vm);
    }
}
