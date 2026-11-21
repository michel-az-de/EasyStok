using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Uma rodada do <see cref="ContaReceberPixReconciliacaoJob"/>: consulta o gateway parcela a parcela e
/// interrompe a rodada quando o gateway parece fora do ar.
/// </summary>
public static class RodadaPixContaReceber
{
    public const int FalhasSeguidasParaInterromper = 3;

    public sealed record Resumo(int Consultadas, int Reconciliadas, int Falhas);

    public static async Task<Resumo> ProcessarAsync(
        IReadOnlyList<ParcelaReceber> parcelas,
        Func<string, CancellationToken, Task<bool>> reconciliar,
        Action descartarAlteracoes,
        ILogger logger,
        CancellationToken ct)
    {
        int consultadas = 0, reconciliadas = 0, falhas = 0, seguidas = 0;

        foreach (var parcela in parcelas)
        {
            if (string.IsNullOrWhiteSpace(parcela.EfiTxid)) continue;
            if (parcela.Status != StatusParcela.Pendente && parcela.Status != StatusParcela.ParcialmentePaga
                && parcela.Status != StatusParcela.Vencida)
                continue;

            try
            {
                var reconciliado = await reconciliar(parcela.EfiTxid, ct);
                consultadas++;
                seguidas = 0;
                if (reconciliado) reconciliadas++;
            }
            catch (Exception ex)
            {
                falhas++;
                seguidas++;
                // #1598: o resto da parcela que falhou não pode sair no commit da seguinte (mesmo escopo).
                descartarAlteracoes();
                logger.LogWarning(ex, "Falha ao reconciliar parcela {ParcelaId} txid={Txid}",
                    parcela.Id, parcela.EfiTxid);
                // #2580: só falha seguida indica gateway fora; a intercalada é da parcela.
                if (seguidas >= FalhasSeguidasParaInterromper)
                {
                    logger.LogWarning("ContaReceberPixReconciliacaoJob: {Seguidas} falhas seguidas, interrompendo rodada.", seguidas);
                    break;
                }
            }
        }

        return new Resumo(consultadas, reconciliadas, falhas);
    }
}
