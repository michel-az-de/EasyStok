using EasyStock.Api.Authorization;
using EasyStock.Api.Services.Impressao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>Corpo de <c>POST api/impressao/{id}/falhou</c>.</summary>
public sealed record FalhaImpressaoRequest(string? Erro);

/// <summary>
/// Canhoto e fila de impressão (S20). A API está na nuvem e não alcança a impressora: o pedido pago entra na
/// fila e um consumidor faz polling. O backend é agnóstico do dispositivo (onda 0.6 decide): bridge local
/// com ESC/POS (header <c>X-Impressao-Api-Key</c>), impressora com polling HTTP ou a aba do console (JWT).
///
/// <para>Fluxo do consumidor:</para>
/// <list type="number">
///   <item><c>GET api/impressao/pendentes?limite=10</c> (mais antigas primeiro).</item>
///   <item><c>GET api/pedidos/{pedidoId}/canhoto?formato=texto</c> (42 colunas, sem acentos) ou <c>html</c> (80 mm).</item>
///   <item><c>POST api/impressao/{id}/impressa</c> (idempotente) ou <c>POST api/impressao/{id}/falhou</c>.</item>
/// </list>
///
/// <para>
/// Fila e canhoto aceitam a policy <c>ImpressaoFila</c> (operador do console ou bridge). Reimprimir é só do
/// operador. O SSE (S18) avisa <c>impressao.pendente</c> e <c>impressao.atrasada</c>.
/// </para>
/// </summary>
[SwaggerTag("Operação: impressão")]
[ApiController]
public sealed class ImpressaoController(
    MontarCanhotoUseCase montarCanhoto,
    ListarImpressoesPendentesUseCase listarPendentes,
    RegistrarRetornoImpressaoUseCase registrarRetorno,
    ReimprimirCanhotoUseCase reimprimir,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    public const string FormatoHtml = "html";
    public const string FormatoTexto = "texto";

    [SwaggerOperation(Summary = "Canhoto de produção do pedido (formato=html|texto)")]
    [HttpGet("api/pedidos/{id:guid}/canhoto")]
    [Authorize(Policy = ImpressaoApiKeyAuthHandler.PolicyFila)]
    public async Task<IActionResult> Canhoto(Guid id, [FromQuery] string? formato, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var f = string.IsNullOrWhiteSpace(formato) ? FormatoHtml : formato.Trim().ToLowerInvariant();
        if (f is not (FormatoHtml or FormatoTexto))
            return DataBadRequest("formato deve ser html ou texto.");

        var canhoto = await montarCanhoto.ExecuteAsync(new MontarCanhotoInput(emp, id), ct);
        if (canhoto is null) return DataNotFound("Pedido não encontrado.");

        return f == FormatoTexto
            ? Content(CanhotoTexto.Formatar(canhoto), "text/plain; charset=utf-8")
            : Content(CanhotoHtml.Renderizar(canhoto), "text/html; charset=utf-8");
    }

    [SwaggerOperation(Summary = "Fila de impressão: pendentes da empresa, mais antigas primeiro")]
    [HttpGet("api/impressao/pendentes")]
    [Authorize(Policy = ImpressaoApiKeyAuthHandler.PolicyFila)]
    public async Task<IActionResult> Pendentes(
        [FromQuery] int limite = ListarImpressoesPendentesUseCase.LimitePadrao,
        [FromQuery] Guid? empresaId = null,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        return DataOk(await listarPendentes.ExecuteAsync(new ListarImpressoesPendentesInput(emp, limite), ct));
    }

    [SwaggerOperation(Summary = "Confirmar que o canhoto saiu (idempotente)")]
    [HttpPost("api/impressao/{id:guid}/impressa")]
    [Authorize(Policy = ImpressaoApiKeyAuthHandler.PolicyFila)]
    public Task<IActionResult> Impressa(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        RegistrarAsync(id, empresaId, impressa: true, erro: null, ct);

    [SwaggerOperation(Summary = "Registrar falha na impressão (a dona reimprime pelo console)")]
    [HttpPost("api/impressao/{id:guid}/falhou")]
    [Authorize(Policy = ImpressaoApiKeyAuthHandler.PolicyFila)]
    public Task<IActionResult> Falhou(Guid id, [FromBody] FalhaImpressaoRequest? request, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        RegistrarAsync(id, empresaId, impressa: false, erro: request?.Erro, ct);

    [SwaggerOperation(Summary = "Pôr o canhoto do pedido de novo na fila")]
    [HttpPost("api/pedidos/{id:guid}/reimprimir")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Reimprimir(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var impressao = await reimprimir.ExecuteAsync(new ReimprimirCanhotoInput(emp, id), ct);
        return impressao is null ? DataNotFound("Pedido não encontrado.") : DataOk(impressao);
    }

    private async Task<IActionResult> RegistrarAsync(Guid id, Guid? empresaId, bool impressa, string? erro, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var impressao = await registrarRetorno.ExecuteAsync(new RegistrarRetornoImpressaoInput(emp, id, impressa, erro), ct);
        return impressao is null ? DataNotFound("Impressão não encontrada.") : DataOk(impressao);
    }
}
