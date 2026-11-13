using EasyStock.Application.UseCases.Common;

namespace EasyStock.Api.Http;

/// <summary>
/// #2018 (estrutura-20): o envelope de erro de uma <see cref="UseCaseValidationException"/>, escrito uma vez. A rota que pega
/// a exceção no controller (<c>DataBadRequest(ex)</c>) e a que deixa subir até o <c>GlobalExceptionHandler</c> respondem
/// o mesmo: <c>code</c> é o da exceção, ou <c>BAD_REQUEST</c> quando ela não traz um; <c>message</c> é o texto do motivo;
/// <c>detail</c> repete o texto (clientes antigos liam o motivo ali); <c>details</c> são os dados estruturados, se houver.
/// </summary>
public static class ApiErrors
{
    public const string CodigoPadraoDeValidacao = "BAD_REQUEST";

    public static ApiErrorResponse Validacao(UseCaseValidationException ex, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var codigo = string.IsNullOrEmpty(ex.Code) ? CodigoPadraoDeValidacao : ex.Code;
        return new ApiErrorResponse(new ApiError(codigo, ex.Message, ex.Message, correlationId) { Details = ex.Details });
    }
}
