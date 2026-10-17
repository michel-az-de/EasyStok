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
        catch (Exception erro) when (context.RequestAborted.IsCancellationRequested && EhCancelamento(erro))
        {
            logger.LogDebug("Requisição cancelada pelo cliente: {Method} {Path}.",
                SanitizarParaLog(context.Request.Method), SanitizarParaLog(CaminhoDoLog.De(context)));

            // Com a resposta já iniciada o status não muda (lançaria); só sobra encerrar.
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusClienteFechouRequisicao;
        }
    }

    // Método e caminho vêm do cliente: sem CR e LF não dá para forjar uma linha de log (CodeQL cs/log-forging, #1877).
    // Mesma regra do ProtecaoCsrfCookieMiddleware.
    private static string SanitizarParaLog(string? valor) =>
        (valor ?? "").Replace("\r", "").Replace("\n", "");

    /// <summary>
    /// O cancelamento chega direto ou embrulhado: o driver do banco e o <c>AggregateException</c> trazem o
    /// <see cref="OperationCanceledException"/> como causa interna.
    /// </summary>
    private static bool EhCancelamento(Exception erro)
    {
        for (var atual = erro; atual is not null; atual = atual.InnerException)
            if (atual is OperationCanceledException) return true;
        return false;
    }
}
