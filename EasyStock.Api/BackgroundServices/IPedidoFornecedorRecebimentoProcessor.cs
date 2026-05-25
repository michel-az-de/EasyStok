namespace EasyStock.Api.BackgroundServices;

public interface IPedidoFornecedorRecebimentoProcessor
{
    Task<int> ProcessAsync(CancellationToken cancellationToken);
}

public sealed class NoOpPedidoFornecedorRecebimentoProcessor(
    ILogger<NoOpPedidoFornecedorRecebimentoProcessor> logger) : IPedidoFornecedorRecebimentoProcessor
{
    public Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "ProcessarRecebimentoJob foi habilitado, mas nenhum processador real de recebimentos foi registrado.");
        return Task.FromResult(0);
    }
}
