using EasyStock.Application.Common;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services.Linkers;

/// <summary>
/// #2030: o que o aparelho sincroniza depois de o console fechar o dia entra no instante do registro, a regra
/// do recebimento do provedor (#1724-12). Na hora do aparelho cairia no dia fechado: o retrato não o contou,
/// a gaveta e a sugestão da próxima abertura mudariam depois do fato e ele não poderia mais ser estornado.
/// O fechamento sem loja é o da empresa e vale também para o lançamento de loja (<c>CaixaFechadoGuard</c>).
/// </summary>
internal static class InstanteNoCaixa
{
    public static async Task<DateTime> DoRegistroAsync(EasyStockDbContext db, Guid empresaId, DateTime instanteUtc, Guid? lojaId) =>
        await DiaFechadoAsync(db, empresaId, instanteUtc, lojaId) ? DateTime.UtcNow : instanteUtc;

    /// <summary>O dia operacional (Brasília) deste instante já tem fechamento da loja ou da empresa (#1598).</summary>
    public static Task<bool> DiaFechadoAsync(EasyStockDbContext db, Guid empresaId, DateTime instanteUtc, Guid? lojaId)
    {
        var dia = HorarioBrasil.DataOperacional(instanteUtc);
        return db.Set<FechamentoCaixa>().IgnoreQueryFilters()
            .AnyAsync(f => f.EmpresaId == empresaId && f.Data == dia && (f.LojaId == lojaId || f.LojaId == null));
    }
}
