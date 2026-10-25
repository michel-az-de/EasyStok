using EasyStock.Application.Ports.Output.Persistence.Pagamentos;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Uma rodada do <see cref="CobrancaPedidoJob"/> sobre as cobranças vencidas. A falha fica na cobrança:
/// outras cobranças da mesma empresa também precisam conferir pagamentos com webhook perdido.
/// O job limita e alterna as páginas para as cobranças antigas com falha não prenderem as seguintes.
/// </summary>
public static class RodadaDeCobrancasVencidas
{
    public sealed record Resumo(int Processadas, int Falhas, int Puladas);

    public static async Task<Resumo> ProcessarAsync(
        IReadOnlyList<CobrancaPedidoVencida> vencidas,
        Func<CobrancaPedidoVencida, CancellationToken, Task> processar,
        ILogger logger,
        CancellationToken ct)
    {
        int processadas = 0, falhas = 0;

        foreach (var item in vencidas)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await processar(item, ct);
                processadas++;
            }
            // #2023: o timeout do HttpClient chega como OperationCanceledException com o ct intacto; é falha da cobrança.
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                falhas++;
                logger.LogWarning(ex, "CobrancaPedidoJob: falha na cobranca {CobrancaId} pedido {PedidoId}",
                    item.CobrancaId, item.PedidoId);
            }
        }

        return new Resumo(processadas, falhas, 0);
    }
}
