using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;

namespace EasyStock.Api.Observability;

public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items["CorrelationId"] as string ?? "unknown";

        var (statusCode, code, title, detail, logAsError) = MapException(exception);

        if (logAsError)
            Log.Error(exception, "Erro inesperado na API. CorrelationId: {CorrelationId}", correlationId);
        else
            Log.Warning(exception, "Erro tratado na API. CorrelationId: {CorrelationId}", correlationId);

        var envelope = new ApiErrorResponse(new ApiError(code, title, detail, correlationId));

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsJsonAsync(envelope, cancellationToken);

        return true;
    }

    private static (int StatusCode, string Code, string Title, string Detail, bool LogAsError) MapException(Exception exception) =>
        exception switch
        {
            // Exce��es espec�ficas que N�O herdam de RegraDeDominioVioladaException
            UseCaseValidationException ex => (
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Requisicao invalida",
                ex.Message,
                false),

            QuantidadeInvalidaException ex => (
                StatusCodes.Status400BadRequest,
                "INVALID_QUANTITY",
                "Quantidade invalida",
                ex.Message,
                false),

            CredenciaisInvalidasException ex => (
                StatusCodes.Status401Unauthorized,
                "UNAUTHORIZED",
                "Credenciais invalidas",
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

            // Exce��es de infraestrutura
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "CONCURRENCY_CONFLICT",
                "Conflito de concorrencia",
                "Os dados foram alterados por outro processo. Recarregue as informacoes e tente novamente.",
                false),

            // Exce��es de dom�nio (todas herdam de RegraDeDominioVioladaException - case gen�rico)
            RegraDeDominioVioladaException ex => (
                StatusCodes.Status409Conflict,
                "BUSINESS_RULE_VIOLATION",
                "Violacao de regra de negocio",
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
