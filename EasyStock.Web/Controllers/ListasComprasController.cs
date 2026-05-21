using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.ListasCompras;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ListasComprasController(ListasComprasService svc, InteligenciaService inteligencia, SessionService session) : BaseController(session)
{
    [HttpGet("/listas-compras")]
    public async Task<IActionResult> Index(string? status = null)
    {
        ViewBag.Title = "Listas de compras";
        ViewBag.ActiveMenuItem = "ListasCompras";

        var vm = new ListasComprasIndexViewModel { FiltroStatus = status };
        var result = await svc.ListarAsync(status);
        if (result.Success && result.Data is not null) vm.Listas = result.Data;

        return View(vm);
    }

    [HttpGet("/listas-compras/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Lista de compras";
        ViewBag.ActiveMenuItem = "ListasCompras";

        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Index));

        return View(new ListaComprasDetailViewModel { Detalhe = result.Data });
    }

    [HttpGet("/listas-compras/gerar")]
    public async Task<IActionResult> Gerar()
    {
        ViewBag.Title = "Gerar lista de compras";
        ViewBag.ActiveMenuItem = "ListasCompras";

        var vm = new GerarListaViewModel { NomeSugerido = $"Reposição {DateTime.Now:dd/MM}" };
        var result = await inteligencia.SugestaoReposicaoListaAsync(50);
        if (result.Success && result.Data is not null) vm.Sugestoes = result.Data;

        return View(vm);
    }

    [HttpPost("/listas-compras/gerar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Gerar(GerarListaForm form)
    {
        var selecionados = form.Itens
            .Where(i => i.Incluir && !string.IsNullOrWhiteSpace(i.Texto))
            .ToList();

        if (selecionados.Count == 0)
        {
            Toast("warning", "Selecione ao menos um item para gerar a lista.");
            return RedirectToAction(nameof(Gerar));
        }

        var nome = string.IsNullOrWhiteSpace(form.Nome) ? $"Reposição {DateTime.Now:dd/MM}" : form.Nome.Trim();
        var itens = selecionados.Select(i => (object)new
        {
            texto = i.Texto!.Trim(),
            produtoId = i.ProdutoId,
            quantidade = i.Quantidade,
            unidade = (string?)null,
            categoria = "Reposição",
            observacao = (string?)null
        });

        var result = await svc.GerarAsync(nome, form.Observacoes, itens);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", $"Lista gerada com {selecionados.Count} {(selecionados.Count == 1 ? "item" : "itens")}.");
        return RedirectToAction(nameof(Detail), new { id = result.Data?.Id });
    }

    [HttpGet("/listas-compras/{id}/imprimir")]
    public async Task<IActionResult> Imprimir(string id)
    {
        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Index));
        return View(new ListaComprasDetailViewModel { Detalhe = result.Data });
    }

    [HttpPost("/listas-compras/{id}/gerar-pedidos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GerarPedidos(string id)
    {
        var result = await svc.GerarPedidosAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Detail), new { id });

        if (result.Data.Pedidos.Count == 0)
        {
            Toast("warning", "Nenhum item da lista tem fornecedor conhecido para virar pedido.");
            return RedirectToAction(nameof(Detail), new { id });
        }

        // PRG: guarda o resultado e redireciona, evitando reenvio (que notificaria fornecedores 2x).
        TempData["PedidosGeradosJson"] = System.Text.Json.JsonSerializer.Serialize(result.Data);
        return RedirectToAction(nameof(PedidosGerados), new { id });
    }

    [HttpGet("/listas-compras/{id}/pedidos-gerados")]
    public IActionResult PedidosGerados(string id)
    {
        if (TempData["PedidosGeradosJson"] is not string json)
            return RedirectToAction(nameof(Detail), new { id });

        var data = System.Text.Json.JsonSerializer.Deserialize<GerarPedidosResultApi>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (data is null) return RedirectToAction(nameof(Detail), new { id });

        ViewBag.Title = "Pedidos gerados";
        ViewBag.ActiveMenuItem = "ListasCompras";
        return View(new PedidosGeradosViewModel { ListaId = id, Resultado = data });
    }

    [HttpPost("/listas-compras")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(string nome, string? observacoes)
    {
        var result = await svc.CriarAsync(nome, observacoes);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", "Lista criada.");
        return RedirectToAction(nameof(Detail), new { id = result.Data?.Id });
    }

    [HttpPost("/listas-compras/{id}/arquivar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Arquivar(string id)
    {
        var result = await svc.ArquivarAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Lista arquivada.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/listas-compras/{id}/reabrir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reabrir(string id)
    {
        var result = await svc.ReabrirAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", "Lista reaberta.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/listas-compras/{id}/itens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(string id, string texto, decimal? quantidade, string? unidade, string? categoria, string? produtoId)
    {
        // Auditoria 2026-04-30 (HIGH fix): validar texto não-vazio.
        if (string.IsNullOrWhiteSpace(texto))
        {
            Toast("error", "Texto do item é obrigatório.");
            return RedirectToAction(nameof(Detail), new { id });
        }

        // produtoId vem do autocomplete (vazio = item de texto livre).
        Guid? pid = Guid.TryParse(produtoId, out var g) ? g : null;
        var result = await svc.AddItemAsync(id, texto, quantidade, unidade, categoria, null, pid);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/listas-compras/{id}/itens/{itemId}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleItem(string id, string itemId, bool done)
    {
        var result = await svc.ToggleItemAsync(id, itemId, done);
        // AJAX (marcar comprado instantâneo): responde 204/500 sem redirect.
        // Sem JS, mantém o fallback de full reload.
        var ajax = Request.Headers["X-Requested-With"].ToString() == "fetch";
        if (HasError(result))
            return ajax ? StatusCode(500) : RedirectToAction(nameof(Detail), new { id });
        return ajax ? NoContent() : RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/listas-compras/{id}/itens/{itemId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(string id, string itemId)
    {
        var result = await svc.RemoveItemAsync(id, itemId);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Item removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }
}
