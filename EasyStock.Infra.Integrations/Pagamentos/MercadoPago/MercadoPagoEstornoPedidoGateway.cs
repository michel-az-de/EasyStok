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

    public async Task<EstornoPedidoResult> EstornarAsync(
        string pagamentoExternoId, decimal valor, string chaveIdempotencia, CancellationToken ct = default)
    {
        try
        {
            var r = await mercadoPago.EstornarAsync(pagamentoExternoId, valor, chaveIdempotencia, ct);
            if (r.Status is "rejected" or "cancelled")
            {
                logger.LogWarning("MP recusou o estorno {Chave}: status {Status}.", chaveIdempotencia, r.Status);
                return EstornoPedidoResult.Falha(CodigoRecusado);
            }
            return EstornoPedidoResult.Ok(r.EstornoId);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Falha no estorno {Chave} no Mercado Pago.", chaveIdempotencia);
            return EstornoPedidoResult.Falha(CodigoRecusado);
        }
    }
}
