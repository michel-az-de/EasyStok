using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EasyStock.Api.Observability;

public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items["CorrelationId"] as string ?? "unknown";

        var (statusCode, title, detail, logAsError) = MapException(exception);

        if (logAsError)
            Log.Error(exception, "Erro inesperado na API. CorrelationId: {CorrelationId}", correlationId);
        else
            Log.Warning(exception, "Erro tratado na API. CorrelationId: {CorrelationId}", correlationId);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static (int StatusCode, string Title, string Detail, bool LogAsError) MapException(Exception exception) =>
        exception switch
        {
            UseCaseValidationException validationException => (
                StatusCodes.Status400BadRequest,
                "Requisicao invalida",
                validationException.Message,
                false),
            QuantidadeInvalidaException quantidadeInvalidaException => (
                StatusCodes.Status400BadRequest,
                "Quantidade invalida",
                quantidadeInvalidaException.Message,
                false),
            ProdutoInativoException produtoInativoException => (
                StatusCodes.Status409Conflict,
                "Produto inativo",
                produtoInativoException.Message,
                false),
            EstoqueInsuficienteException estoqueInsuficienteException => (
                StatusCodes.Status409Conflict,
                "Estoque insuficiente",
                estoqueInsuficienteException.Message,
                false),
            ItemEstoqueBloqueadoException itemBloqueadoException => (
                StatusCodes.Status409Conflict,
                "Item de estoque bloqueado",
                itemBloqueadoException.Message,
                false),
            ItemEstoqueVencidoException itemVencidoException => (
                StatusCodes.Status422UnprocessableEntity,
                "Item de estoque vencido",
                itemVencidoException.Message,
                false),
            ConflitoConcorrenciaException conflitoConcorrenciaException => (
                StatusCodes.Status409Conflict,
                "Conflito de concorrencia de dominio",
                conflitoConcorrenciaException.Message,
                false),
            CredenciaisInvalidasException credenciaisInvalidasException => (
                StatusCodes.Status401Unauthorized,
                "Credenciais invalidas",
                credenciaisInvalidasException.Message,
                false),
            UsuarioNaoAutorizadoException usuarioNaoAutorizadoException => (
                StatusCodes.Status403Forbidden,
                "Acesso negado",
                usuarioNaoAutorizadoException.Message,
                false),
            PlanoLimiteAtingidoException planoLimiteAtingidoException => (
                StatusCodes.Status422UnprocessableEntity,
                "Limite do plano atingido",
                planoLimiteAtingidoException.Message,
                false),
            RegraDeDominioVioladaException regraDeDominioVioladaException => (
                StatusCodes.Status409Conflict,
                "Conflito de negocio",
                regraDeDominioVioladaException.Message,
                false),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Conflito de concorrencia",
                "Os dados foram alterados por outro processo. Recarregue as informacoes e tente novamente.",
                false),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Erro interno do servidor",
                "Ocorreu um erro inesperado. Tente novamente mais tarde.",
                true)
        };
}
