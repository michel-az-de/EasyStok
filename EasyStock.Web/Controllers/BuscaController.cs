using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class BuscaController(BuscaUnificadaService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/busca")]
    public async Task<IActionResult> Buscar(string? q, int limit = 15)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Json(Array.Empty<object>());

        var result = await svc.BuscarAsync(q.Trim(), Math.Min(limit, 50));
        if (!result.Success) return Json(Array.Empty<object>());

        return Json(result.Data!.Select(r => new
        {
            tipo = r.Tipo,
            id = r.Id,
            produtoId = r.ProdutoId,
            produtoVariacaoId = r.ProdutoVariacaoId,
            titulo = r.Titulo,
            subtitulo = r.Subtitulo,
            chaveExibicao = r.ChaveExibicao,
            sku = r.Sku,
            quantidadeAtual = r.QuantidadeAtual,
            status = r.Status,
            fornecedorNome = r.FornecedorNome,
            loja = r.Loja
        }));
    }
}
