using EasyStock.Web.Models.ViewModels.Produtos;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ProdutosController(ProdutosService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/produtos")]
    public async Task<IActionResult> Index(int page = 1, string? search = null, string? categoria = null, string? status = null)
    {
        ViewBag.Title = "Produtos";
        ViewBag.ActiveMenuItem = "Produtos";

        var result = await svc.ListarAsync(page, 20, categoria, status, search);
        if (HasError(result)) return View(new ProdutosListViewModel());

        var paged = result.Data!;
        var vm = new ProdutosListViewModel
        {
            Produtos = paged.Data,
            Search = search,
            Categoria = categoria,
            Status = status,
            Paginacao = new PaginationViewModel
            {
                Page = paged.Meta.Page,
                Pages = paged.Meta.Pages,
                Total = paged.Meta.Total,
                Limit = paged.Meta.Limit
            }
        };
        return View(vm);
    }

    [HttpGet("/produtos/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Produto";
        ViewBag.ActiveMenuItem = "Produtos";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var vm = new ProdutoDetailViewModel { Produto = result.Data! };
        return View(vm);
    }

    [HttpGet("/produtos/novo")]
    public IActionResult Novo()
    {
        ViewBag.Title = "Novo Produto";
        ViewBag.ActiveMenuItem = "Produtos";
        return View("Form", new ProdutoFormViewModel());
    }

    [HttpPost("/produtos/novo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(ProdutoFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Novo Produto";
            ViewBag.ActiveMenuItem = "Produtos";
            return View("Form", vm);
        }

        var result = await svc.CriarAsync(vm);
        if (HasError(result)) return View("Form", vm);

        // Add variações after creation
        if (vm.Variacoes.Count > 0)
        {
            var produtoId = result.Data?.Id;
            if (!string.IsNullOrEmpty(produtoId))
            {
                foreach (var varNome in vm.Variacoes.Where(v => !string.IsNullOrWhiteSpace(v)))
                    await svc.AdicionarVariacaoAsync(produtoId, varNome);
            }
        }

        Toast("success", "Produto criado com sucesso!");
        return RedirectToAction(nameof(Detail), new { id = result.Data?.Id });
    }

    [HttpGet("/produtos/{id}/editar")]
    public async Task<IActionResult> Editar(string id)
    {
        ViewBag.Title = "Editar Produto";
        ViewBag.ActiveMenuItem = "Produtos";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var p = result.Data!;
        var vm = new ProdutoFormViewModel
        {
            Id = p.Id,
            Nome = p.Nome,
            Sku = p.Sku,
            Categoria = p.Categoria,
            Subcategoria = p.Subcategoria,
            Preco = p.Preco,
            Custo = p.Custo,
            Peso = p.Peso,
            Descricao = p.Descricao,
            Emoji = p.Emoji,
            Variacoes = p.Variacoes.Select(v => v.Nome).ToList()
        };
        return View("Form", vm);
    }

    [HttpPost("/produtos/{id}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Atualizar(string id, ProdutoFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Editar Produto";
            ViewBag.ActiveMenuItem = "Produtos";
            return View("Form", vm);
        }

        vm.Id = id;
        var result = await svc.EditarAsync(id, vm);
        if (HasError(result)) return View("Form", vm);

        Toast("success", "Produto atualizado com sucesso!");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/produtos/{id}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(string id)
    {
        var result = await svc.ExcluirAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Produto excluído com sucesso!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/produtos/{id}/foto")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadFoto(string id, IFormFile foto)
    {
        if (foto == null || foto.Length == 0)
        {
            Toast("error", "Selecione uma imagem.");
            return RedirectToAction(nameof(Detail), new { id });
        }

        if (foto.Length > 5 * 1024 * 1024)
        {
            Toast("error", "Imagem não pode ser maior que 5MB.");
            return RedirectToAction(nameof(Detail), new { id });
        }

        var result = await svc.UploadFotoAsync(id, foto);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Foto enviada com sucesso!");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/produtos/{id}/variacoes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdicionarVariacao(string id, string nome)
    {
        var result = await svc.AdicionarVariacaoAsync(id, nome);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Variação adicionada!");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/produtos/{id}/variacoes/{vid}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoverVariacao(string id, string vid)
    {
        var result = await svc.RemoverVariacaoAsync(id, vid);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Variação removida!");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpGet("/produtos/buscar")]
    public async Task<IActionResult> Buscar(string? q, int limit = 10)
    {
        var result = await svc.ListarAsync(1, Math.Min(limit, 100), null, null, q);
        if (!result.Success) return Json(Array.Empty<object>());
        var items = result.Data!.Data.Select(p => new {
            id = p.Id, nome = p.Nome, sku = p.Sku,
            foto = p.Fotos.FirstOrDefault(),
            emoji = p.Emoji,
            categoria = p.Categoria
        });
        return Json(items);
    }
}
