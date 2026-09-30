using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Enums.Operacao;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Operacao;

/// <summary>
/// Persistência da fila de impressão (S20). <c>empresaId</c> no WHERE das consultas de tenant além do
/// filtro global e do RLS (ADR-0010). <see cref="ListarAtrasadasAsync"/> é cross-tenant por natureza (job):
/// <c>IgnoreQueryFilters</c> + bypass de RLS em escopo curto, só leitura.
/// </summary>
public sealed class ImpressaoPendenteRepository(EasyStockDbContext db) : IImpressaoPendenteRepository
{
    public async Task AddAsync(ImpressaoPendente impressao, CancellationToken ct = default) =>
        await db.ImpressoesPendentes.AddAsync(impressao, ct);

    public Task<ImpressaoPendente?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.ImpressoesPendentes.FirstOrDefaultAsync(i => i.EmpresaId == empresaId && i.Id == id, ct);

    public async Task<IReadOnlyList<ImpressaoPendente>> ListarPendentesAsync(Guid empresaId, int limite, CancellationToken ct = default) =>
        await db.ImpressoesPendentes
            .AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && i.Status == StatusImpressao.Pendente)
            .OrderBy(i => i.CriadaEm)
            .ThenBy(i => i.Id)
            .Take(Math.Clamp(limite, 1, 500))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ImpressaoAtrasada>> ListarAtrasadasAsync(
        DateTime criadasDepoisDe, DateTime criadasAntesDe, int maximo, CancellationToken ct = default)
    {
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.ImpressoesPendentes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.Status == StatusImpressao.Pendente
                        && i.CriadaEm >= criadasDepoisDe
                        && i.CriadaEm <= criadasAntesDe)
            .OrderBy(i => i.CriadaEm)
            .Take(Math.Clamp(maximo, 1, 500))
            .Select(i => new ImpressaoAtrasada(i.Id, i.EmpresaId, i.PedidoId, i.CriadaEm))
            .ToListAsync(ct);
    }
}
