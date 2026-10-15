using EasyStock.Application.Common;
using EasyStock.Api.Services.Impressao;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Api.Controllers;

public sealed record PrepararImpressoRequest(Guid SolicitacaoId, string Documento, string Modelo,
    string Codigo = "qr", string Perfil = "Diálogo do sistema", bool LogoTermica = false, Guid? OriginalId = null);

public sealed record PreviaImpressoRequest(string Documento, string Modelo, string Codigo = "qr", bool LogoTermica = false);

[ApiController]
[Authorize(Policy = "Operador")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DocumentosImpressosController(PrepararDocumentoImpressoUseCase preparar,
    IImpressaoPendenteRepository repo, MontarPedidoImpressoUseCase montar, PedidoImpressoHtml renderer,
    MontarComandaUseCase montarComanda, ComandaHtml comandaRenderer, ICurrentUserAccessor usuario,
    IWebHostEnvironment ambiente, IFileStorage storage, IPedidoImpressoQueries casas, ResumoAtendimentoHtml resumoRenderer,
    TimeProvider relogio) : EasyStockControllerBase
{
    [HttpPost("api/pedidos/{id:guid}/impressoes")]
    public async Task<IActionResult> Preparar(Guid id, [FromBody] PrepararImpressoRequest body, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(usuario, null, out var empresa, out var erro)) return erro!;
        if (body.SolicitacaoId == Guid.Empty || string.IsNullOrWhiteSpace(body.Perfil) || body.Perfil.Length > 80
            || !OpcoesValidas(body.Documento, body.Modelo, body.Codigo))
            return DataBadRequest("Documento, papel, código, perfil ou solicitação inválidos.");

        var html = string.Empty;
        // Retry não remonta um documento nem depende novamente dos assets.
        if (body.OriginalId is null && await repo.GetBySolicitacaoAsync(empresa, body.SolicitacaoId, ct) is null)
        {
            var montado = await RenderizarAsync(empresa, id, body.Documento, body.Modelo, body.Codigo, body.LogoTermica, ct);
            if (montado is null) return DataNotFound("Pedido não encontrado.");
            html = montado;
        }
        var resultado = await preparar.ExecuteAsync(new(empresa, id, body.SolicitacaoId,
            usuario.UsuarioId == Guid.Empty ? null : usuario.UsuarioId, body.Documento, body.Modelo,
            body.Codigo, body.Perfil.Trim(), body.LogoTermica, html, "bilhete-1756-3", body.OriginalId), ct);
        return resultado is null ? DataNotFound("Pedido ou impressão original não encontrados.") : DataOk(resultado);
    }

    /// <summary>
    /// Prévia do documento sem guardar tentativa (#1839): o console mostra o papel assim que o modal abre e a
    /// cada troca de opção; só o "Imprimir" grava o registro com <see cref="Preparar"/>.
    /// </summary>
    [HttpPost("api/pedidos/{id:guid}/impressoes/previa")]
    public async Task<IActionResult> Previa(Guid id, [FromBody] PreviaImpressoRequest body, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(usuario, null, out var empresa, out var erro)) return erro!;
        if (!OpcoesValidas(body.Documento, body.Modelo, body.Codigo))
            return DataBadRequest("Documento, papel ou código inválidos.");
        var html = await RenderizarAsync(empresa, id, body.Documento, body.Modelo, body.Codigo, body.LogoTermica, ct);
        return html is null ? DataNotFound("Pedido não encontrado.") : Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// Resumo do atendimento no layout dos impressos (#1839). O console manda o resumo que já calculou no
    /// Encerrar; a API só desenha o papel, com as fontes e o logo embutidos. Não guarda tentativa: o resumo
    /// não é de um pedido e o histórico de impressão é por pedido.
    /// </summary>
    [HttpPost("api/impressao/resumo-atendimento")]
    public async Task<IActionResult> ResumoAtendimento([FromQuery] string? modelo, [FromBody] ResumoAtendimentoImpresso body, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(usuario, null, out var empresa, out var erro)) return erro!;
        var papel = string.IsNullOrWhiteSpace(modelo) ? ImpressoHtml.A4 : modelo.Trim().ToLowerInvariant();
        if (body is null || !ImpressoHtml.Papeis.Contains(papel) || !ResumoAtendimentoHtml.Valido(body))
            return DataBadRequest("Papel ou resumo inválidos.");
        var origem = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var casa = await casas.ObterCasaAsync(empresa, null, ct);
        var agora = HorarioBrasil.ConverterParaBrasilia(relogio.GetUtcNow().UtcDateTime);
        var html = await resumoRenderer.RenderizarAsync(body, casa, papel, origem, agora, ct);
        return Content(await RecursosImpresso.CongelarAsync(html, empresa, origem, ambiente, storage, ct), "text/html; charset=utf-8");
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

    private static bool OpcoesValidas(string? documento, string? modelo, string? codigo) =>
        modelo is not null && ImpressoHtml.Papeis.Contains(modelo) && codigo is not null && CodigoImpresso.Tipos.Contains(codigo)
        && documento is not null && (PedidoImpressoHtml.Documentos.Contains(documento) || documento == "cozinha");

    // Monta o documento com fontes e logo embutidos; null quando o pedido não existe na empresa.
    private async Task<string?> RenderizarAsync(Guid empresa, Guid id, string documento, string modelo, string codigo,
        bool logoTermica, CancellationToken ct)
    {
        var origem = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        string html;
        if (documento == "cozinha")
        {
            var comanda = await montarComanda.ExecuteAsync(new MontarComandaInput(empresa, id), ct);
            if (comanda is null) return null;
            html = await comandaRenderer.RenderizarAsync(comanda, modelo, origem, ct, codigo);
        }
        else
        {
            var pedido = await montar.ExecuteAsync(new MontarPedidoImpressoInput(empresa, id), ct);
            if (pedido is null) return null;
            var alergias = documento == "agendamento"
                ? (await montarComanda.ExecuteAsync(new MontarComandaInput(empresa, id), ct))?.Alergias : null;
            html = await renderer.RenderizarAsync(pedido, documento, modelo, origem,
                new ImpressoOpcoes(logoTermica, alergias, codigo), ct);
        }
        return await RecursosImpresso.CongelarAsync(html, empresa, origem, ambiente, storage, ct);
    }
}
