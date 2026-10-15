using EasyStock.Application.Ports.Output.Persistence.Pagamentos;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Uma rodada do <see cref="CobrancaPedidoJob"/> sobre as cobranças vencidas (I3.2, #1688). A falha conta
/// por loja: com <see cref="FalhasParaPularALoja"/> falhas, o resto das cobranças daquela loja fica para a
/// próxima rodada e as demais lojas seguem. Antes, três falhas quaisquer interrompiam a rodada inteira, e
/// três cobranças presas de uma loja travavam a fila de todas.
/// </summary>
public static class RodadaDeCobrancasVencidas
{
    public const int FalhasParaPularALoja = 3;

    public sealed record Resumo(int Processadas, int Falhas, int Puladas);

    public static async Task<Resumo> ProcessarAsync(
        IReadOnlyList<CobrancaPedidoVencida> vencidas,
        Func<CobrancaPedidoVencida, CancellationToken, Task> processar,
        ILogger logger,
        CancellationToken ct)
    {
        var falhasPorLoja = new Dictionary<Guid, int>();
        int processadas = 0, falhas = 0, puladas = 0;

        foreach (var item in vencidas)
        {
            ct.ThrowIfCancellationRequested();
            if (falhasPorLoja.GetValueOrDefault(item.EmpresaId) >= FalhasParaPularALoja)
            {
                puladas++;
                continue;
            }

            try
            {
                await processar(item, ct);
                processadas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                falhas++;
                var daLoja = falhasPorLoja[item.EmpresaId] = falhasPorLoja.GetValueOrDefault(item.EmpresaId) + 1;
                logger.LogWarning(ex, "CobrancaPedidoJob: falha na cobranca {CobrancaId} pedido {PedidoId}",
                    item.CobrancaId, item.PedidoId);
                if (daLoja == FalhasParaPularALoja)
                    logger.LogWarning("CobrancaPedidoJob: {Falhas} falhas na loja {EmpresaId}; o resto dela fica para a proxima rodada.",
                        daLoja, item.EmpresaId);
            }
        }

        return new Resumo(processadas, falhas, puladas);
    }
}