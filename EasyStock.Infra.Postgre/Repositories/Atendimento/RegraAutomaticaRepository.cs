using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class RegraAutomaticaRepository(EasyStockDbContext db) : IRegraAutomaticaRepository
{
    public Task AddAsync(RegraAutomatica regra, CancellationToken ct = default) =>
        db.RegrasAutomaticas.AddAsync(regra, ct).AsTask();

    public Task<RegraAutomatica?> ObterPorGatilhoAsync(Guid empresaId, GatilhoAutomacao gatilho, CancellationToken ct = default) =>
        db.RegrasAutomaticas.FirstOrDefaultAsync(r => r.EmpresaId == empresaId && r.Gatilho == gatilho, ct);

    public async Task<IReadOnlyList<RegraAutomatica>> ListarAsync(Guid empresaId, CancellationToken ct = default) =>
        await db.RegrasAutomaticas.AsNoTracking().Where(r => r.EmpresaId == empresaId).OrderBy(r => r.Gatilho).ToListAsync(ct);
}
