using System.Globalization;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.ImpressaoLoja;
using FluentValidation.AspNetCore;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Regra de impressão da loja (#2199; spec impressão v2, D2): "como sai" cada documento, igual no PWA, no APK e na Web.
/// Concorrência otimista por versão: o GET devolve <c>ETag: "n"</c> e o PUT exige <c>If-Match</c> com ela.
/// </summary>
[SwaggerTag("Stores / Lojas")]
[ApiController]
[Route("api/lojas/{lojaId:guid}/impressao")]
[Authorize]
public sealed class LojaImpressaoController(
    ObterRegraImpressaoLojaUseCase obterUseCase,
    GravarRegraImpressaoLojaUseCase gravarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    public const string CodigoConflito = "IMPRESSAO_CONFLITO";
    public const string CodigoSemIfMatch = "IMPRESSAO_SEM_IF_MATCH";

    /// <summary>A regra canônica completa tem menos de 1 KB; 8 KB folga para espaços e chaves a mais sem abrir amplificação.</summary>
    public const int TamanhoMaximoCorpo = 8 * 1024;

    [SwaggerOperation(Summary = "Regra de impressão da loja", Description = "ETag \"n\" é a versão; impressao null = nunca configurada.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Operador")]
    [HttpGet]
    public async Task<IActionResult> Obter(Guid lojaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var error)) return error!;

        var regra = await obterUseCase.ExecuteAsync(new ObterRegraImpressaoLojaQuery(empresaId, lojaId), ct);
        if (regra is null) return DataNotFound("Loja não encontrada.");

        Response.Headers.ETag = ETag(regra.Versao);
        return DataOk(regra);
    }

    [SwaggerOperation(
        Summary = "Grava a regra de impressão da loja",
        Description = "Exige If-Match com a ETag do GET. Mesmo corpo responde 200 sem mudar a versão; corpo novo sobre versão velha responde 409 com o estado atual.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [Authorize(Policy = "Gerente")]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    [HttpPut]
    public async Task<IActionResult> Gravar(
        Guid lojaId,
        // O use case valida e responde IMPRESSAO_INVALIDA com o caminho de cada campo: um formato só de 400 para o cliente.
        [FromBody, CustomizeValidator(Skip = true)] RegraImpressaoLojaEntrada regra,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var error)) return error!;

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new ApiErrorResponse(new ApiError(
                CodigoSemIfMatch, "Envie If-Match com a ETag do GET (\"0\" se a loja nunca configurou).", null, null)));
        }

        if (!TryLerVersao(ifMatch, out var versao))
            return DataBadRequest("If-Match inválido: use a ETag do GET, por exemplo \"3\".");

        GravarRegraImpressaoResult resultado;
        try
        {
            resultado = await gravarUseCase.ExecuteAsync(new GravarRegraImpressaoCommand(empresaId, lojaId, versao, regra), ct);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }

        switch (resultado.Desfecho)
        {
            case DesfechoGravacaoImpressao.LojaNaoEncontrada:
                return DataNotFound("Loja não encontrada.");
            case DesfechoGravacaoImpressao.Conflito:
                if (resultado.Atual is not null) Response.Headers.ETag = ETag(resultado.Atual.Versao);
                return Conflict(new ApiErrorResponse(new ApiError(
                    CodigoConflito, "A regra de impressão mudou depois da sua leitura. Recarregue e refaça a alteração.", null, null)
                {
                    Details = new { atual = resultado.Atual },
                }));
            default:
                Response.Headers.ETag = ETag(resultado.Atual!.Versao);
                return DataOk(resultado.Atual);
        }
    }

    private static string ETag(int versao) => $"\"{versao.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>Aceita <c>"3"</c>, <c>W/"3"</c> e <c>3</c>. Lista de ETags e <c>*</c> não valem: a versão tem que ser uma.</summary>
    internal static bool TryLerVersao(string ifMatch, out int versao)
    {
        var valor = ifMatch.Trim();
        if (valor.StartsWith("W/", StringComparison.Ordinal)) valor = valor[2..];
        if (valor.Length >= 2 && valor[0] == '"' && valor[^1] == '"') valor = valor[1..^1];
        return int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out versao);
    }
}
