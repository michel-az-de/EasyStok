using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Número curto do Lote do ERP para o lote do aparelho (#2209, F4 da impressão v2), o da barra Code128 C.
/// O lote do aparelho chega ao ERP por <c>mobile_batches.ErpLoteId</c>; o lote antigo, sem número, ganha um aqui.
/// Falhar não derruba o sync: sem número a etiqueta sai só com o QR, como antes.
/// </summary>
public sealed class NumeroCurtoDosLotesMobile(
    EasyStockDbContext db,
    ILoteNumeroCurtoRepository numeros,
    ILogger<NumeroCurtoDosLotesMobile> log)
{
    /// <summary>
    /// Por id do lote do aparelho, o número do Lote ligado a ele. Só lotes da empresa e da loja do aparelho (ou sem
    /// loja): o id vem do payload, então um aparelho não lê nem numera o lote de outra loja.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> DosLotesDoAparelhoAsync(
        Guid empresaId, Guid lojaId, IReadOnlyCollection<string> batchIds, CancellationToken ct)
    {
        if (batchIds.Count == 0) return new Dictionary<string, string>();
        try
        {
            var ids = batchIds.Distinct().ToList();
            var ligados = await db.Set<Batch>().AsNoTracking()
                .Where(b => b.EmpresaId == empresaId && (b.LojaId == lojaId || b.LojaId == null)
                    && ids.Contains(b.Id) && b.ErpLoteId != null)
                .Select(b => new { b.Id, LoteId = b.ErpLoteId!.Value })
                .ToListAsync(ct);
            if (ligados.Count == 0) return new Dictionary<string, string>();

            var porLote = await GarantirAsync(empresaId, ligados.Select(l => l.LoteId).ToList(), ct);
            return ligados
                .Where(l => porLote.ContainsKey(l.LoteId))
                .ToDictionary(l => l.Id, l => porLote[l.LoteId]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Número curto dos lotes do aparelho falhou; as etiquetas saem só com o QR.");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Por id do Lote do ERP, o número dele. Para o pull, que já tem o <c>ErpLoteId</c> em mãos.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> DosLotesAsync(
        Guid empresaId, IReadOnlyCollection<Guid> loteIds, CancellationToken ct)
    {
        if (loteIds.Count == 0) return new Dictionary<Guid, string>();
        try
        {
            return await GarantirAsync(empresaId, loteIds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Número curto dos lotes do pull falhou; as etiquetas saem só com o QR.");
            return new Dictionary<Guid, string>();
        }
    }

    /// <summary>Lotes antigos numerados por chamada: segura o primeiro pull completo de uma loja com muitos lotes.</summary>
    public const int MaximoNovosPorChamada = 200;

    // Lê de uma vez os números dados e numera o lote antigo um a um, cada um no próprio comando, para que dois
    // aparelhos numerando os mesmos lotes não se travem. Um lote que falha fica sem barra e não leva os outros junto.
    private async Task<IReadOnlyDictionary<Guid, string>> GarantirAsync(
        Guid empresaId, IReadOnlyCollection<Guid> loteIds, CancellationToken ct)
    {
        var ids = loteIds.Distinct().ToList();
        var dados = new Dictionary<Guid, string>(await numeros.NumerosDadosAsync(empresaId, ids, ct));
        var semNumero = ids.Where(id => !dados.ContainsKey(id)).ToList();
        foreach (var id in semNumero.Take(MaximoNovosPorChamada))
        {
            try
            {
                if (await numeros.GarantirAsync(empresaId, id, ct) is { } numero) dados[id] = numero;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Número curto do lote {LoteId} falhou; a etiqueta dele sai só com o QR.", id);
            }
        }
        if (semNumero.Count > MaximoNovosPorChamada)
            log.LogInformation("{Restantes} lotes antigos ficam sem número nesta chamada (teto {Teto}).",
                semNumero.Count - MaximoNovosPorChamada, MaximoNovosPorChamada);
        return dados;
    }
}
