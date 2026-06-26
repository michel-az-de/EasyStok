using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class AssinaturaEmpresaRepository(EasyStockDbContext dbContext) : IAssinaturaEmpresaRepository
{
    public Task<AssinaturaEmpresa?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.AssinaturasEmpresa
            .Include(a => a.Plano)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IEnumerable<AssinaturaEmpresa>> GetByEmpresaAsync(Guid empresaId) =>
        await dbContext.AssinaturasEmpresa
            .AsNoTracking()
            .Include(a => a.Plano)
            .Where(a => a.EmpresaId == empresaId)
            .ToListAsync();

    public Task<AssinaturaEmpresa?> GetAtivaAsync(Guid empresaId) =>
        dbContext.AssinaturasEmpresa
            .Include(a => a.Plano)
            .FirstOrDefaultAsync(a => a.EmpresaId == empresaId && a.Status == StatusAssinatura.Ativa);

    public Task<AssinaturaEmpresa?> GetMaisRecenteAsync(Guid empresaId) =>
        dbContext.AssinaturasEmpresa
            .Where(a => a.EmpresaId == empresaId)
            .OrderByDescending(a => a.DataInicio)
            .FirstOrDefaultAsync();

    public Task<AssinaturaEmpresa?> GetAtivaMaisRecenteAsync(Guid empresaId) =>
        dbContext.AssinaturasEmpresa
            .Include(a => a.Plano)
            .Where(a => a.EmpresaId == empresaId && a.Status == StatusAssinatura.Ativa)
            .OrderByDescending(a => a.DataInicio)
            .FirstOrDefaultAsync();

    public Task AddAsync(AssinaturaEmpresa assinatura) =>
        dbContext.AssinaturasEmpresa.AddAsync(assinatura).AsTask();

    public Task UpdateAsync(AssinaturaEmpresa assinatura)
    {
        dbContext.AssinaturasEmpresa.Update(assinatura);
        return Task.CompletedTask;
    }

    // NOTA (issue 694): estes 3 metodos sao chamados SO pelo CobrancaAssinaturaJob, que roda
    // cross-tenant sem JWT. IgnoreQueryFilters desliga o filtro EF de tenant; o job tambem
    // precisa de db.UseRowLevelSecurityBypass() (camada RLS do Postgres). Sem os dois, a query
    // retorna 0 linhas (CurrentTenantId=Guid.Empty) e o job vira no-op.
    public async Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencendoEmAsync(int diasAte, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var limite = now.AddDays(diasAte);
        return await dbContext.AssinaturasEmpresa
            .IgnoreQueryFilters()
            .Include(a => a.Empresa)
            .Where(a => a.Status == StatusAssinatura.Ativa &&
                ((a.TrialFim != null && a.TrialFim >= now && a.TrialFim <= limite) ||
                 (a.DataFim != null && a.DataFim >= now && a.DataFim <= limite)))
            .ToListAsync(ct);
    }

    // Planos PAGOS vencidos -> suspensao (inadimplencia). Antes incluia (TrialFim < now), o que
    // suspenderia clientes pagantes com TrialFim no passado (TrialFim nunca e limpo na conversao).
    // Agora so pega lapso de plano pago (DataFim no passado); trial nao convertido vai para
    // GetTrialsExpiradosAsync.
    public async Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencidasAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await dbContext.AssinaturasEmpresa
            .IgnoreQueryFilters()
            .Where(a => a.Status == StatusAssinatura.Ativa
                     && a.DataFim != null && a.DataFim < now)
            .ToListAsync(ct);
    }

    // Trial vencido SEM nenhum plano pago (DataFim nulo): teste nao convertido -> Expirada.
    // Plano pago vigente (DataFim >= now) nunca e pego aqui.
    public async Task<IEnumerable<AssinaturaEmpresa>> GetTrialsExpiradosAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await dbContext.AssinaturasEmpresa
            .IgnoreQueryFilters()
            .Where(a => a.Status == StatusAssinatura.Ativa
                     && a.DataFim == null
                     && a.TrialFim != null && a.TrialFim < now)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AssinaturaEmpresa>> GetSuspensasAntigasAsync(int diasMinimos, CancellationToken ct = default)
    {
        var limite = DateTime.UtcNow.AddDays(-diasMinimos);
        return await dbContext.AssinaturasEmpresa
            .IgnoreQueryFilters()
            .Where(a => a.Status == StatusAssinatura.Suspensa
                     && ((a.SuspensaEm != null && a.SuspensaEm < limite)
                      || (a.SuspensaEm == null && a.AlteradoEm < limite)))
            .ToListAsync(ct);
    }

    public async Task<decimal> SomarPrecoMensalAtivasAsync(Guid? empresaId = null, CancellationToken ct = default)
    {
        var q = dbContext.AssinaturasEmpresa
            .IgnoreQueryFilters()
            .Where(a => a.Status == StatusAssinatura.Ativa);
        if (empresaId.HasValue && empresaId.Value != Guid.Empty)
            q = q.Where(a => a.EmpresaId == empresaId.Value);

        // JOIN para puxar PrecoMensal do plano vinculado.
        return await q
            .Join(dbContext.Planos, a => a.PlanoId, p => p.Id, (a, p) => p.PrecoMensal)
            .SumAsync(ct);
    }

    public async Task<IReadOnlyDictionary<StatusAssinatura, int>> ContarPorStatusAsync(Guid? empresaId = null, CancellationToken ct = default)
    {
        var q = dbContext.AssinaturasEmpresa
            .IgnoreQueryFilters()
            .AsQueryable();
        if (empresaId.HasValue && empresaId.Value != Guid.Empty)
            q = q.Where(a => a.EmpresaId == empresaId.Value);

        var rows = await q
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Status, r => r.Count);
    }
}
