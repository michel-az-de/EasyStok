using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>Persistência das ocorrências (S27). <c>empresaId</c> no WHERE além do filtro global e do RLS (ADR-0010).</summary>
public sealed class OcorrenciaRepository(EasyStockDbContext db) : IOcorrenciaRepository
{
    public async Task AddAsync(Ocorrencia ocorrencia, CancellationToken ct = default) =>
        await db.Ocorrencias.AddAsync(ocorrencia, ct);

    public Task<Ocorrencia?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.Ocorrencias.FirstOrDefaultAsync(o => o.EmpresaId == empresaId && o.Id == id, ct);

    public async Task<IReadOnlyList<Ocorrencia>> ListarAsync(Guid empresaId, StatusOcorrencia? status, int limite, CancellationToken ct = default)
    {
        var q = db.Ocorrencias.AsNoTracking().Where(o => o.EmpresaId == empresaId);
        if (status is { } s) q = q.Where(o => o.Status == s);
        return await q.OrderByDescending(o => o.CriadaEm).ThenBy(o => o.Id)
            .Take(Math.Clamp(limite, 1, 500))
            .ToListAsync(ct);
    }
}
