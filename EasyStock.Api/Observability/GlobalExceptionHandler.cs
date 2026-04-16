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
        // Cliente desconectou — nao ha resposta a enviar
        if (exception is OperationCanceledException or TaskCanceledException)
        {
            logger.LogDebug(exception, "Requisicao cancelada pelo cliente.");
            return true;
        }

        var correlationId = httpContext.Items["CorrelationId"] as string ?? "unknown";
        var requestPath = httpContext.Request.Path + httpContext.Request.QueryString;
        var requestMethod = httpContext.Request.Method;

        var (statusCode, code, title, detail, logAsError) = MapException(exception);

        if (logAsError)
            logger.LogError(exception,
                "Erro inesperado na API. CorrelationId: {CorrelationId} | {Method} {Path} | Exception: {ExceptionType}: {ExceptionMessage} | InnerException: {InnerException} | StackTrace: {StackTrace}",
                correlationId, requestMethod, requestPath,
                exception.GetType().FullName, exception.Message,
                exception.InnerException?.Message ?? "(nenhuma)",
                exception.StackTrace ?? "(sem stack trace)");
        else
            logger.LogWarning(exception,
                "Erro tratado na API. CorrelationId: {CorrelationId} | {Method} {Path} | {ExceptionType}: {ExceptionMessage}",
                correlationId, requestMethod, requestPath,
                exception.GetType().FullName, exception.Message);

        // Em produção, nunca expor detalhes de exceção 5xx ao cliente (vaza informações sensíveis)
        var errorDetail = detail;
        if (statusCode >= 500)
        {
            var isDevelopment = httpContext.RequestServices?
                .GetService<IWebHostEnvironment>()?.IsDevelopment() ?? false;

            errorDetail = isDevelopment
                ? $"{exception.GetType().Name}: {exception.Message}" +
                  (exception.InnerException is not null
                      ? $" → {exception.InnerException.GetType().Name}: {exception.InnerException.Message}"
                      : string.Empty)
                : "Ocorreu um erro inesperado. Use o CorrelationId para rastreamento.";
        }

        var envelope = new ApiErrorResponse(new ApiError(code, title, errorDetail, correlationId));

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

            // Excecoes padrao .NET mapeadas para HTTP semantico
            KeyNotFoundException ex => (
                StatusCodes.Status404NotFound,
                "NOT_FOUND",
                "Recurso nao encontrado",
                ex.Message,
                false),

            ArgumentNullException ex => (
                StatusCodes.Status400BadRequest,
                "BAD_REQUEST",
                "Argumento invalido",
                ex.Message,
                false),

            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                "BAD_REQUEST",
                "Argumento invalido",
                ex.Message,
                false),

            FormatException ex => (
                StatusCodes.Status400BadRequest,
                "BAD_REQUEST",
                "Formato invalido",
                ex.Message,
                false),

            InvalidOperationException ex => (
                StatusCodes.Status409Conflict,
                "INVALID_OPERATION",
                "Operacao invalida",
                ex.Message,
                false),

            NotSupportedException ex => (
                StatusCodes.Status501NotImplemented,
                "NOT_SUPPORTED",
                "Operacao nao suportada",
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
