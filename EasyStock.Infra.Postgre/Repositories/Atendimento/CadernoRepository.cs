using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class CadernoRepository(EasyStockDbContext db) : ICadernoRepository
{
    public Task AddAsync(TrechoCaderno trecho, CancellationToken ct = default) =>
        db.CadernoTrechos.AddAsync(trecho, ct).AsTask();

    public Task<TrechoCaderno?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.CadernoTrechos.FirstOrDefaultAsync(t => t.EmpresaId == empresaId && t.Id == id, ct);

    public async Task<IReadOnlyList<TrechoCaderno>> ListarAsync(Guid empresaId, bool incluirArquivados, CancellationToken ct = default)
    {
        var query = db.CadernoTrechos.AsNoTracking().Where(t => t.EmpresaId == empresaId);
        if (!incluirArquivados) query = query.Where(t => !t.Arquivado);
        return await query.OrderBy(t => t.Titulo).ThenBy(t => t.Id).ToListAsync(ct);
    }
}
