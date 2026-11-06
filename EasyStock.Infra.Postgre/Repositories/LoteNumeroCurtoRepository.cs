using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Número curto do lote antigo (#2199, D4). O UPDATE só pega o lote ainda sem número e chama a mesma função do trigger,
/// então o contador da loja avança uma vez. Roda sob o RLS do tenant: lote de outra empresa não é visto.
/// </summary>
public sealed class LoteNumeroCurtoRepository(EasyStockDbContext db) : ILoteNumeroCurtoRepository
{
    public async Task<string?> GarantirAsync(Guid empresaId, Guid loteId, CancellationToken ct = default)
    {
        // O SELECT do CTE enxerga o lote como estava antes do UPDATE: devolve o número já existente ou, pelo UPDATE,
        // o que acabou de ser dado.
        var numero = (await db.Database.SqlQuery<string>($"""
            WITH dado AS (
                UPDATE lotes SET "NumeroCurto" = lote_numero_curto_proximo("EmpresaId", "LojaId")
                WHERE "Id" = {loteId} AND "EmpresaId" = {empresaId} AND "NumeroCurto" IS NULL
                RETURNING "NumeroCurto"
            )
            SELECT "NumeroCurto" AS "Value" FROM dado
            UNION ALL
            SELECT "NumeroCurto" AS "Value" FROM lotes
            WHERE "Id" = {loteId} AND "EmpresaId" = {empresaId} AND "NumeroCurto" IS NOT NULL
            """).ToListAsync(ct)).FirstOrDefault();
        if (numero is not null) return numero;

        // Corrida: outra chamada deu o número entre a leitura deste comando e o UPDATE dele, que então não achou o lote
        // sem número. Uma leitura nova já enxerga o número dado.
        return (await db.Database.SqlQuery<string>($"""
            SELECT "NumeroCurto" AS "Value" FROM lotes
            WHERE "Id" = {loteId} AND "EmpresaId" = {empresaId} AND "NumeroCurto" IS NOT NULL
            """).ToListAsync(ct)).FirstOrDefault();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NumerosDadosAsync(
        Guid empresaId, IReadOnlyCollection<Guid> loteIds, CancellationToken ct = default)
    {
        var ids = loteIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();

        return await db.Lotes.AsNoTracking()
            .Where(l => l.EmpresaId == empresaId && ids.Contains(l.Id) && l.NumeroCurto != null)
            .Select(l => new { l.Id, l.NumeroCurto })
            .ToDictionaryAsync(l => l.Id, l => l.NumeroCurto!, ct);
    }
}
