using EasyStock.Web.Models.ViewModels.Fornecedores;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class CriarFornecedorRequest
{
    public string Nome { get; set; } = "";
    public string? Documento { get; set; }
    public string? Contato { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public int? LeadTimeEstimadoDias { get; set; }
    public string? Tipo { get; set; }
    public string? Categoria { get; set; }
    public string? SiteUrl { get; set; }
    public string? PedidoMinimo { get; set; }
    public string? FretePadrao { get; set; }
    public string? Observacoes { get; set; }
}

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

        if (result.Success && result.Data is not null)
            vm.Items = result.Data;

        return View(vm);
    }

    [HttpGet("/fornecedores/novo")]
    public IActionResult Novo()
    {
        ViewBag.Title = "Fornecedores";
        ViewBag.ActiveMenuItem = "Fornecedores";
        // Redirect to index — the "novo" form is a modal on the listing page
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/fornecedores/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Fornecedor";
        ViewBag.ActiveMenuItem = "Fornecedores";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        if (result.Data is null) return RedirectToAction(nameof(Index));
        var vm = new FornecedorDetailViewModel { Fornecedor = result.Data };

        var historicoResult = await svc.ObterHistoricoAsync(id);
        if (historicoResult.Success && historicoResult.Data is not null)
            vm.Historico = historicoResult.Data;

        var estatisticasResult = await svc.ObterEstatisticasAsync(id);
        if (estatisticasResult.Success && estatisticasResult.Data is not null)
        {
            var stats = estatisticasResult.Data;
            vm.TotalGasto = stats.TotalGasto;
            vm.LeadRealMedio = stats.LeadTimeRealMedioDias;
            vm.QuantidadePedidos = stats.QuantidadePedidos;
        }

        // Onda P4 — trail de alterações.
        var alteracoesResult = await svc.ObterAlteracoesAsync(id);
        if (alteracoesResult.Success && alteracoesResult.Data is not null)
            vm.Alteracoes = alteracoesResult.Data;

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
        return RedirectToAction(nameof(Detail), new { id = result.Data?.Id });
    }

    [HttpPost("/fornecedores/json")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarJson([FromBody] CriarFornecedorRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Nome))
            return BadRequest(new { success = false, errorMessage = "Nome é obrigatório." });

        var result = await svc.CriarAsync(
            req.Nome, req.Documento, req.Contato, req.Email, req.Telefone,
            req.LeadTimeEstimadoDias, req.Tipo, req.Categoria, req.SiteUrl,
            req.PedidoMinimo, req.FretePadrao, req.Observacoes);

        if (!result.Success)
            return BadRequest(new { success = false, errorMessage = result.ErrorMessage ?? "Erro ao criar fornecedor." });

        return Ok(new { success = true, id = result.Data?.Id });
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

        Toast("success", "Fornecedor desativado.", $"/fornecedores/{id}/reativar");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/fornecedores/{id}/reativar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reativar(string id)
    {
        var result = await svc.ReativarAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Fornecedor reativado.");
        return RedirectToAction(nameof(Detail), new { id });
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

        var fornResult = await svc.ListarAsync();
        if (fornResult.Success)
            vm.Fornecedores = fornResult.Data!;

        return View(vm);
    }

    [HttpPost("/fornecedores/pedidos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarPedido(
        string fornecedorId, DateOnly dataPedido, DateOnly? previsaoEntrega,
        decimal? valorEstimado, string? canal, string? observacoes)
    {
        if (string.IsNullOrWhiteSpace(fornecedorId))
        {
            Toast("error", "Selecione um fornecedor.");
            return RedirectToAction(nameof(PedidosAbertos));
        }

        var result = await svc.CriarPedidoAsync(fornecedorId, dataPedido, previsaoEntrega, valorEstimado, canal, observacoes);
        if (HasError(result)) return RedirectToAction(nameof(PedidosAbertos));

        Toast("success", "Pedido criado com sucesso!");
        return RedirectToAction(nameof(PedidosAbertos));
    }

    [HttpPost("/fornecedores/pedidos/{id}/receber")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReceberPedido(string id, string? tracking)
    {
        var result = await svc.ReceberPedidoAsync(id, tracking);
        if (HasError(result)) return RedirectToAction(nameof(PedidosAbertos));

        Toast("success", "Pedido marcado como recebido!");
        return RedirectToAction(nameof(PedidosAbertos));
    }

    [HttpPost("/fornecedores/pedidos/{id}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarPedido(string id)
    {
        var result = await svc.CancelarPedidoAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(PedidosAbertos));

        Toast("success", "Pedido cancelado.");
        return RedirectToAction(nameof(PedidosAbertos));
    }
}
