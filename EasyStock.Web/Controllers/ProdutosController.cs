using EasyStock.Web.Models.Api;
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

        List<ProdutoResumo> items;
        bool hasMore = false;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchResult = await svc.BuscarAsync(search.Trim(), 100);
            if (HasError(searchResult)) return View(new ProdutosListViewModel());
            items = searchResult.Data!;
        }
        else
        {
            var result = await svc.ListarAsync(page, 21);
            if (HasError(result)) return View(new ProdutosListViewModel());
            items = result.Data!;
            hasMore = items.Count > 20;
            if (hasMore) items = items.Take(20).ToList();
        }

        var vm = new ProdutosListViewModel
        {
            Produtos = items,
            Search = search,
            Categoria = categoria,
            Status = status,
            Paginacao = new PaginationViewModel
            {
                Page = page,
                Pages = hasMore ? page + 1 : page,
                Total = items.Count,
                Limit = 20
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
    public async Task<IActionResult> Novo()
    {
        ViewBag.Title = "Novo Produto";
        ViewBag.ActiveMenuItem = "Produtos";
        await LoadCategoriasAsync();
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
            await LoadCategoriasAsync();
            return View("Form", vm);
        }

        var result = await svc.CriarAsync(vm);
        if (HasError(result))
        {
            await LoadCategoriasAsync();
            return View("Form", vm);
        }

        Toast("success", "Produto criado com sucesso!");
        return RedirectToAction(nameof(Detail), new { id = result.Data?.ProdutoId });
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
            Id = p.ProdutoId.ToString(),
            Nome = p.Nome,
            SkuBase = p.SkuBase,
            CodigoBarras = p.CodigoBarras,
            CategoriaId = p.CategoriaId,
            SubcategoriaId = p.SubcategoriaId,
            DescricaoBase = p.DescricaoBase,
            Marca = p.Marca,
            PrecoReferencia = p.PrecoReferencia,
            CustoReferencia = p.CustoReferencia,
            MargemEstimada = p.MargemEstimada,
            Status = p.Status,
            Tipo = p.Tipo,
            ControlaValidade = p.ControlaValidade,
            DimensoesPeso = p.Dimensoes?.Peso,
            DimensoesLargura = p.Dimensoes?.Largura,
            DimensoesAltura = p.Dimensoes?.Altura,
            DimensoesComprimento = p.Dimensoes?.Comprimento,
            VariacoesRich = p.Variacoes.Select(v => new VariacaoFormItem
            {
                Nome = v.Nome,
                Cor = v.Cor,
                Tamanho = v.Tamanho,
                DescricaoComercial = v.DescricaoComercial,
                Sku = v.Sku,
                CodigoBarras = v.CodigoBarras,
                Ativa = v.Ativa
            }).ToList(),
            Caracteristicas = p.Caracteristicas.Select(c => new CaracteristicaFormItem
            {
                Nome = c.Nome,
                Descricao = c.Descricao,
                QuantidadeReferencia = c.QuantidadeReferencia,
                VariacaoPadrao = c.VariacaoPadrao,
                OrdemExibicao = c.OrdemExibicao
            }).ToList(),
            Embalagens = p.Embalagens.Select(e => new EmbalagemFormItem
            {
                Nome = e.Nome,
                Descricao = e.Descricao,
                Peso = e.Dimensoes?.Peso,
                Largura = e.Dimensoes?.Largura,
                Altura = e.Dimensoes?.Altura,
                Comprimento = e.Dimensoes?.Comprimento,
                Padrao = e.Padrao
            }).ToList()
        };
        await LoadCategoriasAsync();
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
            await LoadCategoriasAsync();
            return View("Form", vm);
        }

        vm.Id = id;
        var result = await svc.EditarAsync(id, vm);
        if (HasError(result))
        {
            await LoadCategoriasAsync();
            return View("Form", vm);
        }

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
    public async Task<IActionResult> AdicionarVariacao(string id, string nome, string? sku = null)
    {
        var result = await svc.AdicionarVariacaoAsync(id, nome, sku);
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

    [HttpGet("/produtos/{id}/historico-json")]
    public async Task<IActionResult> HistoricoJson(string id)
    {
        var result = await svc.HistoricoAsync(id);
        if (!result.Success) return Json(Array.Empty<object>());
        return Json(result.Data);
    }

    [HttpGet("/produtos/buscar")]
    public async Task<IActionResult> Buscar(string? q, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q)) return Json(Array.Empty<object>());

        var result = await svc.BuscarAsync(q, Math.Min(limit, 100));
        if (!result.Success) return Json(Array.Empty<object>());

        var items = result.Data!.Select(p => new {
            id = p.Id,
            nome = p.Nome,
            sku = p.SkuBase?.Value,
            categoriaId = p.CategoriaId
        });
        return Json(items);
    }

    private async Task LoadCategoriasAsync()
    {
        var cats = await svc.ListarCategoriasAsync();
        ViewBag.Categorias = cats.Success ? cats.Data : [];
    }
}
