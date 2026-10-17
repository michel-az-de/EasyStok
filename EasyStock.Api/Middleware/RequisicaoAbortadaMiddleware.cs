using EasyStock.Api.Observability.Logging;

namespace EasyStock.Api.Middleware;

/// <summary>
/// Requisição abortada pelo cliente (aba fechada, tela trocada, polling cancelado) interrompe o pedido com
/// <see cref="OperationCanceledException"/> e <c>RequestAborted</c> cancelado. Não é falha da API: a exceção
/// passava pelo log de requisição do Serilog, que grava Error "responded 500", e escondia o erro de verdade
/// no ruído (#1874). Aqui vira 499 (client closed request) e um log de Debug, sem exceção.
///
/// <para>
/// Fica logo abaixo do log de requisição, para ele registrar o 499. Cancelamento com <c>RequestAborted</c>
/// vivo (timeout interno, token ligado a outro prazo) e qualquer outra exceção seguem para o tratamento
/// global como antes.
/// </para>
/// </summary>
public sealed class RequisicaoAbortadaMiddleware(RequestDelegate next, ILogger<RequisicaoAbortadaMiddleware> logger)
{
    /// <summary>Convenção do nginx para "o cliente fechou a requisição"; ninguém lê a resposta.</summary>
    public const int StatusClienteFechouRequisicao = 499;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Requisição cancelada pelo cliente: {Method} {Path}.", context.Request.Method, CaminhoDoLog.De(context));

            // Com a resposta já iniciada o status não muda (lançaria); só sobra encerrar.
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusClienteFechouRequisicao;
        }
    }
}
