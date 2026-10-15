using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class RespostaProntaRepository(EasyStockDbContext db) : IRespostaProntaRepository
{
    public Task AddAsync(RespostaPronta resposta, CancellationToken ct = default) =>
        db.RespostasProntas.AddAsync(resposta, ct).AsTask();

    public Task<RespostaPronta?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.RespostasProntas.FirstOrDefaultAsync(r => r.EmpresaId == empresaId && r.Id == id, ct);

    public async Task<IReadOnlyList<RespostaPronta>> ListarAsync(Guid empresaId, bool incluirArquivadas, CancellationToken ct = default)
    {
        var query = db.RespostasProntas.AsNoTracking().Where(r => r.EmpresaId == empresaId);
        if (!incluirArquivadas) query = query.Where(r => !r.Arquivada);
        return await query.OrderBy(r => r.Atalho).ToListAsync(ct);
    }

    public Task<bool> ExisteAtalhoAsync(Guid empresaId, string atalho, Guid? excetoId, CancellationToken ct = default) =>
        db.RespostasProntas.AnyAsync(r => r.EmpresaId == empresaId && r.Atalho == atalho
            && (excetoId == null || r.Id != excetoId), ct);
}
