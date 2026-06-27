using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Observability;
using EasyStock.Application.UseCases.Common;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Diagnostics;

namespace EasyStock.Api.Observability;

public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IOperationalMetrics metrics) : IExceptionHandler
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

        // Métrica de falha de operação: só 5xx (erros realmente inesperados/infra), rotulada
        // pelo `code` mapeado — domínio fechado em 5xx ({INTERNAL_ERROR, NOT_SUPPORTED}), sem
        // explosão de cardinalidade. Único ponto de emissão (fora de transação/retry).
        if (statusCode >= 500)
            metrics.IncrementFalhasOperacao(code);

        // Persiste erros 5xx no SystemErrorLog para rastreabilidade na tela de Diagnóstico.
        // Fire-and-forget: não bloqueia a resposta HTTP.
        if (statusCode >= 500)
        {
            _ = TrySaveSystemErrorLogAsync(
                httpContext.RequestServices,
                requestMethod, requestPath.ToString(), correlationId,
                exception);
        }

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

        // UseCaseValidationException pode trazer Code + Details estruturados (ex: CYCLE_DETECTED).
        // Quando setados, sobrescrevem o code generico "VALIDATION_ERROR".
        var effectiveCode = code;
        object? effectiveDetails = null;
        if (exception is UseCaseValidationException ucvex)
        {
            if (!string.IsNullOrEmpty(ucvex.Code))
                effectiveCode = ucvex.Code;
            effectiveDetails = ucvex.Details;
        }

        var apiError = new ApiError(effectiveCode, title, errorDetail, correlationId);
        if (exception is EasyStock.Domain.Exceptions.PlanoLimiteAtingidoException planEx)
            apiError = apiError with { Recurso = planEx.Recurso };
        if (effectiveDetails is not null)
            apiError = apiError with { Details = effectiveDetails };
        var envelope = new ApiErrorResponse(apiError);

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

            // Falha de autorizacao expressa via UnauthorizedAccessException (reports, SLA,
            // helpdesk: comentar/atribuir/encaminhar). Sem este case caia no default 500 —
            // negacao de permissao virava "erro interno" e poluia system_error_logs.
            UnauthorizedAccessException ex => (
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

            // Postgres unique constraint violation (23505) — duplicação detectada
            // só na hora do INSERT (corrida de 2 requests). Mapear pra 409.
            DbUpdateException dbex when IsUniqueViolation(dbex) => (
                StatusCodes.Status409Conflict,
                "DUPLICATE_RESOURCE",
                "Registro duplicado",
                "Já existe um registro com esses dados (SKU, email, documento ou outro campo único).",
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
                LimparSufixoParametro(ex),
                false),

            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                "BAD_REQUEST",
                "Argumento invalido",
                LimparSufixoParametro(ex),
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

    // ArgumentException.Message anexa " (Parameter 'x')" automaticamente (.NET). Esse sufixo
    // tecnico vaza o nome do parametro para o usuario (BUG-08 do QA). Removemos na borda,
    // preservando a mensagem de negocio e o status 400 — cobre todos os ArgumentException
    // user-facing (Dinheiro, Gtin, Telefone, etc.) sem alterar o dominio.
    private static string LimparSufixoParametro(ArgumentException ex) =>
        string.IsNullOrEmpty(ex.ParamName)
            ? ex.Message
            : Regex.Replace(ex.Message, @"\s*\(Parameter '[^']*'\)\s*$", string.Empty);

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is Npgsql.PostgresException pg && pg.SqlState == "23505") return true;
            inner = inner.InnerException;
        }
        return false;
    }

    private static async Task TrySaveSystemErrorLogAsync(
        IServiceProvider services,
        string method, string path, string correlationId,
        Exception exception)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SystemErrorLogs.Add(new SystemErrorLog
            {
                Id = Guid.NewGuid(),
                Source = "api_backend",
                Level = "error",
                Category = "api_exception",
                Message = $"{exception.GetType().Name}: {exception.Message}",
                Details = JsonSerializer.Serialize(new
                {
                    exceptionType = exception.GetType().FullName,
                    message = exception.Message,
                    innerException = exception.InnerException?.Message,
                    stackTrace = exception.StackTrace?.Split('\n').Take(10),
                    requestMethod = method,
                    requestPath = path
                }),
                CorrelationId = correlationId,
                Url = $"{method} {path}",
                CriadoEm = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch
        {
            // Best-effort — nunca lançar aqui para não mascarar o erro original.
        }
    }
}
