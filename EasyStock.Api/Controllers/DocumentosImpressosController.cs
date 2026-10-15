using EasyStock.Api.Services.Impressao;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Api.Controllers;

public sealed record PrepararImpressoRequest(Guid SolicitacaoId, string Documento, string Modelo,
    string Codigo = "qr", string Perfil = "Diálogo do sistema", bool LogoTermica = false, Guid? OriginalId = null);

[ApiController]
[Authorize(Policy = "Operador")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DocumentosImpressosController(PrepararDocumentoImpressoUseCase preparar,
    IImpressaoPendenteRepository repo, MontarPedidoImpressoUseCase montar, PedidoImpressoHtml renderer,
    MontarComandaUseCase montarComanda, ComandaHtml comandaRenderer, ICurrentUserAccessor usuario,
    IWebHostEnvironment ambiente, IFileStorage storage) : EasyStockControllerBase
{
    [HttpPost("api/pedidos/{id:guid}/impressoes")]
    public async Task<IActionResult> Preparar(Guid id, [FromBody] PrepararImpressoRequest body, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(usuario, null, out var empresa, out var erro)) return erro!;
        if (body.SolicitacaoId == Guid.Empty || string.IsNullOrWhiteSpace(body.Perfil) || body.Perfil.Length > 80
            || !ImpressoHtml.Papeis.Contains(body.Modelo) || !CodigoImpresso.Tipos.Contains(body.Codigo)
            || !(PedidoImpressoHtml.Documentos.Contains(body.Documento) || body.Documento == "cozinha"))
            return DataBadRequest("Documento, papel, código, perfil ou solicitação inválidos.");

        var html = string.Empty;
        // Retry não remonta um documento nem depende novamente dos assets.
        if (body.OriginalId is null && await repo.GetBySolicitacaoAsync(empresa, body.SolicitacaoId, ct) is null)
        {
            var origem = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            if (body.Documento == "cozinha")
            {
                var comanda = await montarComanda.ExecuteAsync(new MontarComandaInput(empresa, id), ct);
                if (comanda is null) return DataNotFound("Pedido não encontrado.");
                html = await comandaRenderer.RenderizarAsync(comanda, body.Modelo, origem, ct, body.Codigo);
            }
            else
            {
                var pedido = await montar.ExecuteAsync(new MontarPedidoImpressoInput(empresa, id), ct);
                if (pedido is null) return DataNotFound("Pedido não encontrado.");
                var alergias = body.Documento == "agendamento"
                    ? (await montarComanda.ExecuteAsync(new MontarComandaInput(empresa, id), ct))?.Alergias : null;
                html = await renderer.RenderizarAsync(pedido, body.Documento, body.Modelo, origem,
                    new ImpressoOpcoes(body.LogoTermica, alergias, body.Codigo), ct);
            }
            html = await RecursosImpresso.CongelarAsync(html, empresa, origem, ambiente, storage, ct);
        }
        var resultado = await preparar.ExecuteAsync(new(empresa, id, body.SolicitacaoId,
            usuario.UsuarioId == Guid.Empty ? null : usuario.UsuarioId, body.Documento, body.Modelo,
            body.Codigo, body.Perfil.Trim(), body.LogoTermica, html, "bilhete-1756-3", body.OriginalId), ct);
        return resultado is null ? DataNotFound("Pedido ou impressão original não encontrados.") : DataOk(resultado);
    }

    [HttpGet("api/pedidos/{id:guid}/impressoes")]
    public async Task<IActionResult> Historico(Guid id, [FromQuery] int pagina = 1, CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(usuario, null, out var empresa, out var erro)) return erro!;
        return DataOk(await repo.HistoricoAsync(empresa, id, pagina, ct));
    }

    [HttpGet("api/pedidos/{pedidoId:guid}/impressoes/{id:guid}/html")]
    public async Task<IActionResult> Html(Guid pedidoId, Guid id, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(usuario, null, out var empresa, out var erro)) return erro!;
        var i = await repo.GetByIdAsync(empresa, id, ct);
        return i is null || i.EmpresaId != empresa || i.PedidoId != pedidoId || i.Html is null
            ? DataNotFound("Impressão não encontrada.") : Content(i.Html, "text/html; charset=utf-8");
    }

}

