using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

/// <summary>
/// Estorno de pedido pelo Mercado Pago (S27 sobre o <see cref="IMercadoPagoClient.EstornarAsync"/> da S32).
/// A chave de idempotência é o id da ocorrência: repetir a resolução não estorna duas vezes. Erro HTTP ou
/// estorno <c>rejected</c>/<c>cancelled</c> viram falha limpa; a ocorrência continua aberta.
/// </summary>
public sealed class MercadoPagoEstornoPedidoGateway(
    IMercadoPagoClient mercadoPago,
    ILogger<MercadoPagoEstornoPedidoGateway> logger) : IEstornoPedidoGateway
{
    public const string CodigoRecusado = "estorno_recusado";

    /// <summary>Sem resposta conclusiva: o estorno pode ter sido efetivado (I3.1, invariante 13).</summary>
    public const string CodigoIndeterminado = "estorno_indeterminado";

    /// <summary>Credencial ausente ou recusada (401, 403): nada foi estornado e não é recusa do reembolso.</summary>
    public const string CodigoSemCredencial = "mercado_pago_sem_credencial";

    public async Task<EstornoPedidoResult> EstornarAsync(
        string pagamentoExternoId, decimal valor, string chaveIdempotencia, CancellationToken ct = default)
    {
        try
        {
            var r = await mercadoPago.EstornarAsync(pagamentoExternoId, valor, chaveIdempotencia, ct);
            var confere = !string.IsNullOrWhiteSpace(r.EstornoId) && r.Valor == valor;

            if (confere && r.Status == EstornoMercadoPagoResult.Simulado)
                return EstornoPedidoResult.Simulado(r.EstornoId);
            if (confere && r.Status == PagamentoMercadoPago.Approved)
                return EstornoPedidoResult.Ok(r.EstornoId);

            if (r.Status is "rejected" or "cancelled")
            {
                logger.LogWarning("MP recusou o estorno {Chave}: status {Status}.", chaveIdempotencia, r.Status);
                return EstornoPedidoResult.Falha(CodigoRecusado);
            }

            // Em análise, sem status ou resposta que não confere com o pedido: não é recusa.
            logger.LogWarning("Estorno {Chave} sem confirmação no Mercado Pago: status {Status}.", chaveIdempotencia, r.Status);
            return EstornoPedidoResult.Indeterminado(CodigoIndeterminado);
        }
        catch (HttpRequestException ex) when (ex is MercadoPagoNaoConfiguradoException
                                              || ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            // Sem credencial ou credencial recusada: o estorno não foi feito, e não foi o provedor que negou.
            logger.LogError("Estorno {Chave} não enviado: Mercado Pago sem credencial válida.", chaveIdempotencia);
            return EstornoPedidoResult.Falha(CodigoSemCredencial);
        }
        catch (HttpRequestException ex) when (EhRecusa(ex))
        {
            logger.LogWarning("MP recusou o estorno {Chave}: HTTP {Status}.", chaveIdempotencia, (int)ex.StatusCode!.Value);
            return EstornoPedidoResult.Falha(CodigoRecusado);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Timeout, 5xx, rede ou corpo ilegível: a escrita pode ter chegado ao provedor.
            logger.LogWarning("Estorno {Chave} sem resposta conclusiva do Mercado Pago ({Excecao}).",
                chaveIdempotencia, ex.GetType().Name);
            return EstornoPedidoResult.Indeterminado(CodigoIndeterminado);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Estorno {Chave} com argumento inválido.", chaveIdempotencia);
            return EstornoPedidoResult.Falha(CodigoRecusado);
        }
    }

    /// <summary>4xx que diz "não" (exceto 408 e 429, que não dizem nada sobre o estorno).</summary>
    private static bool EhRecusa(HttpRequestException ex) =>
        ex.StatusCode is { } s && (int)s is >= 400 and < 500
        && s is not (System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests);
}