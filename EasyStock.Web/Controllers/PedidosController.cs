using EasyStock.Web.Models.ViewModels.Pedidos;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class CriarPedidoWebRequest
{
    public Guid? ClienteId { get; set; }
    public string? NomeAdHoc { get; set; }
    public string? AptAdHoc { get; set; }
    public string? TelefoneAdHoc { get; set; }
    public string? Observacoes { get; set; }
    public List<CriarItemInput>? Itens { get; set; }
}

public class PedidosController(
    PedidosService svc,
    ClientesService clientesSvc,
    ProdutosService produtosSvc,
    SessionService session) : BaseController(session)
{
    [HttpGet("/pedidos")]
    public async Task<IActionResult> Index(string? search = null, string? status = null)
    {
        ViewBag.Title = "Pedidos";
        ViewBag.ActiveMenuItem = "Pedidos";

        var vm = new PedidosListViewModel { Search = search, FiltroStatus = status };

        var result = await svc.ListarAsync(status, search: search);
        if (result.Success && result.Data is not null) vm.Items = result.Data;

        var cli = await clientesSvc.ListarAsync(status: "ativo");
        if (cli.Success && cli.Data is not null) vm.Clientes = cli.Data;

        // Categorias para o cadastro rápido de produto inline no modal Novo pedido.
        var cats = await produtosSvc.ListarCategoriasAsync();
        if (cats.Success && cats.Data is not null) vm.Categorias = cats.Data;

        return View(vm);
    }

    [HttpGet("/pedidos/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Pedido";
        ViewBag.ActiveMenuItem = "Pedidos";

        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Index));

        return View(new PedidoDetailViewModel { Detalhe = result.Data });
    }

    /// <summary>
    /// Onda P5.C — Recibo print-friendly. View renderiza HTML otimizado pra
    /// CTRL+P → "Salvar como PDF" do browser. Sem dependência de lib de PDF.
    /// </summary>
    [HttpGet("/pedidos/{id}/recibo")]
    public async Task<IActionResult> Recibo(string id)
    {
        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Detail), new { id });

        ViewBag.Title = "Recibo";
        return View(new PedidoDetailViewModel { Detalhe = result.Data });
    }

    [HttpPost("/pedidos/json")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarJson([FromBody] CriarPedidoWebRequest req)
    {
        if (req.ClienteId == null && string.IsNullOrWhiteSpace(req.NomeAdHoc))
            return BadRequest(new { success = false, errorMessage = "Informe um cliente OU um nome ad-hoc." });

        var result = await svc.CriarAsync(req.ClienteId, req.NomeAdHoc, req.AptAdHoc, req.TelefoneAdHoc,
            req.Observacoes, req.Itens);
        if (!result.Success)
            return BadRequest(new { success = false, errorMessage = result.ErrorMessage ?? "Erro ao criar pedido." });

        return Ok(new { success = true, id = result.Data?.Id });
    }

    [HttpPost("/pedidos/{id}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarStatus(string id, string status)
    {
        var result = await svc.AtualizarStatusAsync(id, status);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", $"Pedido marcado como {status}.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(string id, string? motivo)
    {
        var result = await svc.CancelarAsync(id, motivo);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Pedido cancelado.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/pedidos/{id}/itens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(string id, string nome, decimal quantidade, decimal precoUnitario,
        Guid? produtoId, string? emoji, string? unidade, string? observacao)
    {
        var result = await svc.AdicionarItemAsync(id, nome, quantidade, precoUnitario,
            produtoId, emoji, unidade, observacao);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Item adicionado.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/itens/{itemId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(string id, string itemId)
    {
        var result = await svc.RemoverItemAsync(id, itemId);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Item removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/pagamentos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPagamento(string id, string metodo, decimal valor, string? referencia, string? observacao)
    {
        var result = await svc.RegistrarPagamentoAsync(id, metodo, valor, referencia, observacao);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", $"Pagamento {metodo} de {valor:C} registrado.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/pagamentos/{pagamentoId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePagamento(string id, string pagamentoId)
    {
        var result = await svc.RemoverPagamentoAsync(id, pagamentoId);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Pagamento removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }
}
