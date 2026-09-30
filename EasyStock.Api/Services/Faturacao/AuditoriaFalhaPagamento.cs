using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Services.Faturacao;

/// <summary>
/// Implementacao de <see cref="IFalhaPagamentoNotifier"/>: registra a falha
/// como <see cref="TipoEventoFatura.PagamentoFalhou"/> na fatura. A abertura
/// automatica de ticket saiu com o helpdesk (P03, #1116).
/// </summary>
public sealed class AuditoriaFalhaPagamento(
    EasyStockDbContext db,
    ILogger<AuditoriaFalhaPagamento> logger) : IFalhaPagamentoNotifier
{
    public async Task RegistrarFalhaAsync(
        Guid empresaId, Guid? faturaId, string motivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo)) motivo = "(sem motivo)";

        if (!faturaId.HasValue || faturaId.Value == Guid.Empty)
        {
            logger.LogWarning(
                "AuditoriaFalhaPagamento: falha sem fatura linkada. EmpresaId={EmpresaId} Motivo={Motivo}",
                empresaId, motivo);
            return;
        }

        var fatura = await db.Faturas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == faturaId.Value, ct);
        if (fatura is null)
        {
            logger.LogWarning("AuditoriaFalhaPagamento: fatura {FaturaId} nao encontrada.", faturaId);
            return;
        }

        db.FaturaEventos.Add(FaturaEvento.Criar(
            fatura.Id,
            TipoEventoFatura.PagamentoFalhou,
            origem: "auto-ticket",
            valorDepois: motivo));
        await db.SaveChangesAsync(ct);
    }
}
