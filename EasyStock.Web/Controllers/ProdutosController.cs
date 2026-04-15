using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Produtos;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ProdutosController(ProdutosService svc, SessionService session) : BaseController(session)
{
    private const int PageSize = 20;

    [HttpGet("/produtos")]
    public async Task<IActionResult> Index(int page = 1, string? search = null, string? categoria = null, string? status = null)
    {
        ViewBag.Title = "Produtos";
        ViewBag.ActiveMenuItem = "Produtos";

        List<ProdutoResumo> items;
        int totalItems = 0;
        int totalPages = 1;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchResult = await svc.BuscarAsync(search.Trim(), 100);
            if (HasError(searchResult)) return View(new ProdutosListViewModel());
            items = searchResult.Data!;
            totalItems = items.Count;
            totalPages = 1;
        }
        else
        {
            var result = await svc.ListarAsync(page, PageSize);
            if (HasError(result)) return View(new ProdutosListViewModel());
            var paged = result.Data!;
            items = paged.Data;
            totalItems = paged.Meta.Total;
            totalPages = paged.Meta.Pages;
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
                Pages = totalPages,
                Total = totalItems,
                Limit = PageSize
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

    [HttpGet("/produtos/check-sku")]
    public async Task<IActionResult> CheckSku(string sku, string? ignoreProdutoId = null)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return Json(new { disponivel = true });

        var result = await svc.BuscarAsync(sku.Trim(), 5);
        if (!result.Success)
            return Json(new { disponivel = (bool?)null, error = result.ErrorMessage ?? "Erro ao verificar SKU" });

        var exact = result.Data!.Any(p =>
            string.Equals(p.SkuBase?.Value, sku.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (ignoreProdutoId == null || !string.Equals(p.Id.ToString(), ignoreProdutoId, StringComparison.OrdinalIgnoreCase)));

        return Json(new { disponivel = !exact });
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

        if (!result.Success && result.HttpStatus is not (200 or 201))
        {
            HasError(result);
            await LoadCategoriasAsync();
            return View("Form", vm);
        }

        var newId = result.Data?.ProdutoId ?? Guid.Empty;
        if (newId != Guid.Empty)
        {
            Toast("success", "Produto criado com sucesso!");
            return RedirectToAction(nameof(Detail), new { id = newId });
        }

        Toast("success", "Produto criado com sucesso!");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/produtos/{id}/editar")]
    public async Task<IActionResult> Editar(string id)
    {
        ViewBag.Title = "Editar Produto";
        ViewBag.ActiveMenuItem = "Produtos";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var p = result.Data!;
        var embPadrao = p.Embalagens.FirstOrDefault(e => e.Padrao) ?? p.Embalagens.FirstOrDefault();
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
            }).ToList(),
            ExistingPhotos = p.Fotos
        };
        await LoadCategoriasAsync();
        return View("Form", vm);
    }

    [HttpPost("/produtos/{id}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Atualizar(string id, ProdutoFormViewModel vm)
    {
        var isFetch = Request.Headers["X-Fetch"] == "1";

        if (!ModelState.IsValid)
        {
            if (isFetch)
            {
                var msg = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                    ?? "Dados inválidos.";
                return BadRequest(new { erro = msg });
            }
            ViewBag.Title = "Editar Produto";
            ViewBag.ActiveMenuItem = "Produtos";
            await LoadCategoriasAsync();
            return View("Form", vm);
        }

        vm.Id = id;
        var result = await svc.EditarAsync(id, vm);
        if (!result.Success)
        {
            var msg = result.ErrorMessage ?? "Ocorreu um erro inesperado.";
            if (isFetch)
                return BadRequest(new { erro = msg });
            Toast("error", msg);
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
        if (!result.Success)
        {
            var msg = result.ErrorMessage ?? "Erro ao excluir produto.";
            Toast("error", msg.Contains("estoque") ? "Produto possui estoque disponível. Zere o estoque antes de excluir." : msg);
            return RedirectToAction(nameof(Detail), new { id });
        }

        Toast("success", "Produto excluído.", $"/produtos/{id}/restaurar");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/produtos/{id}/restaurar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restaurar(string id)
    {
        var result = await svc.RestaurarAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Produto restaurado.");
        return RedirectToAction(nameof(Detail), new { id });
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

        if (foto.Length > 10 * 1024 * 1024)
        {
            Toast("error", "Imagem nao pode ser maior que 10MB.");
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

        Toast("success", "Variação removida.", $"/produtos/{id}/variacoes/{vid}/restaurar");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/produtos/{id}/variacoes/{vid}/restaurar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestaurarVariacao(string id, string vid)
    {
        var result = await svc.RestaurarVariacaoAsync(id, vid);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Variação restaurada.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpGet("/produtos/{id}/historico-json")]
    public async Task<IActionResult> HistoricoJson(string id)
    {
        var result = await svc.HistoricoAsync(id);
        if (!result.Success) return Json(Array.Empty<object>());
        return Json(result.Data);
    }

    [HttpGet("/produtos/{id}/alteracoes-json")]
    public async Task<IActionResult> AlteracoesJson(string id)
    {
        var result = await svc.AlteracoesAsync(id);
        if (!result.Success) return Json(Array.Empty<object>());
        return Json(result.Data);
    }

    /// <summary>Upload de foto via AJAX — retorna JSON {ok, fotoId, url}</summary>
    [HttpPost("/produtos/{id}/fotos/ajax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadFotoAjax(string id, IFormFile foto)
    {
        if (foto == null || foto.Length == 0)
            return Json(new { ok = false, erro = "Selecione uma imagem." });
        if (foto.Length > 10 * 1024 * 1024)
            return Json(new { ok = false, erro = "Imagem nao pode ser maior que 10MB." });

        var uploadResult = await svc.UploadFotoAsync(id, foto);
        if (!uploadResult.Success)
            return Json(new { ok = false, erro = uploadResult.ErrorMessage ?? "Erro ao enviar foto." });

        // Extrair fotoId e url diretamente do resultado da API (evita re-fetch com race condition)
        if (uploadResult.Data is System.Text.Json.JsonElement je)
        {
            var fotoId = je.TryGetProperty("fileId", out var fid) ? fid.GetString()
                       : je.TryGetProperty("FileId", out var fid2) ? fid2.GetString()
                       : null;
            var url = je.TryGetProperty("url", out var u) ? u.GetString()
                    : je.TryGetProperty("Url", out var u2) ? u2.GetString()
                    : null;
            if (fotoId is not null && url is not null)
                return Json(new { ok = true, fotoId, url });
        }

        // Fallback: re-busca o produto (menos seguro para uploads concorrentes)
        var prod = await svc.ObterAsync(id);
        if (prod.Success && prod.Data?.Fotos.Count > 0)
        {
            var ultima = prod.Data.Fotos.Last();
            return Json(new { ok = true, fotoId = ultima.FotoId, url = ultima.Url });
        }
        return Json(new { ok = false, erro = "Foto enviada, mas nao foi possivel obter os dados. Recarregue a pagina." });
    }

    /// <summary>Remoção de foto via AJAX — retorna JSON {ok}</summary>
    [HttpPost("/produtos/{id}/fotos/{fotoId}/remover")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoverFotoAjax(string id, string fotoId)
    {
        var result = await svc.RemoverFotoAsync(id, fotoId);
        if (!result.Success)
            return Json(new { ok = false, erro = result.ErrorMessage ?? "Erro ao remover foto." });
        return Json(new { ok = true });
    }

    /// <summary>Reordenação de fotos via AJAX — retorna JSON {ok}</summary>
    [HttpPost("/produtos/{id}/fotos/reordenar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReordenarFotos(string id, [FromBody] Guid[] novaOrdem)
    {
        var result = await svc.ReordenarFotosAsync(id, novaOrdem);
        if (!result.Success)
            return Json(new { ok = false, erro = result.ErrorMessage ?? "Erro ao reordenar fotos." });
        return Json(new { ok = true });
    }

    [HttpPost("/produtos/{id}/preco-rapido")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrecoRapido(string id, decimal preco)
    {
        var result = await svc.AtualizarPrecoAsync(id, preco);
        if (!result.Success)
            return Json(new { ok = false, erro = result.ErrorMessage ?? "Erro ao atualizar preço." });
        return Json(new { ok = true });
    }

    [HttpGet("/produtos/marcas-sugestoes")]
    public async Task<IActionResult> MarcasSugestoes(string? q)
    {
        var result = await svc.ListarMarcasAsync(q);
        if (!result.Success) return Json(Array.Empty<string>());
        return Json(result.Data ?? []);
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
            marca = p.Marca,
            fotoUrl = p.PrimeiraFotoUrl,
            categoriaId = p.CategoriaId,
            custoReferencia = p.CustoReferencia?.Valor,
            precoReferencia = p.PrecoReferencia?.Valor
        });
        return Json(items);
    }

    private async Task LoadCategoriasAsync()
    {
        var cats = await svc.ListarCategoriasAsync();
        ViewBag.Categorias = cats.Success ? cats.Data : [];
    }

}
