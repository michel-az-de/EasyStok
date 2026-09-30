using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

/// <summary>
/// Estorno de pedido enquanto o <c>EstornarAsync</c> do Mercado Pago (S32) não chega ao master.
/// Recusa limpo com <see cref="Codigo"/>: a ocorrência continua aberta e a dona devolve pelo painel do
/// Mercado Pago. Substituir por um adaptador sobre <see cref="IMercadoPagoClient"/> quando a S32 entrar.
/// </summary>
public sealed class EstornoPedidoIndisponivelGateway(ILogger<EstornoPedidoIndisponivelGateway> logger) : IEstornoPedidoGateway
{
    public const string Codigo = "estorno_indisponivel";

    public Task<EstornoPedidoResult> EstornarAsync(
        string pagamentoExternoId, decimal valor, string chaveIdempotencia, CancellationToken ct = default)
    {
        logger.LogWarning("Estorno automático indisponível (S32 pendente); ocorrência {Chave} fica aberta.", chaveIdempotencia);
        return Task.FromResult(EstornoPedidoResult.Falha(Codigo));
    }
}
