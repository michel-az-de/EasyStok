using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EasyStock.Api.Observability;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items["CorrelationId"] as string ?? "unknown";

        var (statusCode, code, title, detail, logAsError) = MapException(exception);

        if (logAsError)
            logger.LogError(exception, "Erro inesperado na API. CorrelationId: {CorrelationId}", correlationId);
        else
            logger.LogWarning(exception, "Erro tratado na API. CorrelationId: {CorrelationId}", correlationId);

        var envelope = new ApiErrorResponse(new ApiError(code, title, detail, correlationId));

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsJsonAsync(envelope, cancellationToken);

        return true;
    }

    private static (int StatusCode, string Code, string Title, string Detail, bool LogAsError) MapException(Exception exception) =>
        exception switch
        {
            // Exceções específicas que NÃO herdam de RegraDeDominioVioladaException
            UseCaseValidationException ex => (
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Requisição inválida",
                ex.Message,
                false),

            QuantidadeInvalidaException ex => (
                StatusCodes.Status400BadRequest,
                "INVALID_QUANTITY",
                "Quantidade inválida",
                ex.Message,
                false),

            CredenciaisInvalidasException ex => (
                StatusCodes.Status401Unauthorized,
                "UNAUTHORIZED",
                "Credenciais inválidas",
                ex.Message,
                false),

            UsuarioNaoAutorizadoException ex => (
                StatusCodes.Status403Forbidden,
                "FORBIDDEN",
                "Acesso negado",
                ex.Message,
                false),

            PlanoLimiteAtingidoException ex => (
                StatusCodes.Status402PaymentRequired,
                "PLAN_LIMIT_REACHED",
                "Limite do plano atingido",
                ex.Message,
                false),

            // Exceções de infraestrutura
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "CONCURRENCY_CONFLICT",
                "Conflito de concorrência",
                "Os dados foram alterados por outro processo. Recarregue as informações e tente novamente.",
                false),

            // Exceções de domínio (todas herdam de RegraDeDominioVioladaException - case genérico)
            RegraDeDominioVioladaException ex => (
                StatusCodes.Status409Conflict,
                "BUSINESS_RULE_VIOLATION",
                "Violação de regra de negócio",
                ex.Message,
                false),

            _ => (
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "Erro interno do servidor",
                "Ocorreu um erro inesperado. Tente novamente mais tarde.",
                true)
        };
}
