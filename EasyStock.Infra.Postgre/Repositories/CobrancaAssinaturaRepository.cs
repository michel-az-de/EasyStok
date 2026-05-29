using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class CobrancaAssinaturaRepository(EasyStockDbContext dbContext) : ICobrancaAssinaturaRepository
{
    public Task AddAsync(CobrancaAssinatura cobranca) =>
        dbContext.CobrancasAssinatura.AddAsync(cobranca).AsTask();

    public Task UpdateAsync(CobrancaAssinatura cobranca)
    {
        dbContext.CobrancasAssinatura.Update(cobranca);
        return Task.CompletedTask;
    }

    public Task<CobrancaAssinatura?> GetByTxidAsync(string txid) =>
        dbContext.CobrancasAssinatura
            .FirstOrDefaultAsync(c => c.Txid == txid);

    public async Task<CobrancaAssinatura?> GetByTxidComLockAsync(string txid, CancellationToken ct = default)
    {
        // Txid e UUID v4 unico globalmente (gerado pelo PSP no PIX, contrato Sicredi/EFI/etc).
        // Webhook do PSP nao carrega contexto de tenant — busca apenas por Txid.
        // .IgnoreQueryFilters(): sem isso o filtro global de tenant (CurrentTenantId)
        // bloqueia o lookup quando o webhook roda fora de qualquer sessao de usuario
        // (current_tenant = NULL nao casa com EmpresaId persistido).
        const string sql = "SELECT * FROM \"CobrancasAssinatura\" WHERE \"Txid\" = {0} FOR UPDATE";
        return await dbContext.CobrancasAssinatura
            .FromSqlRaw(sql, txid)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct);
    }

    public Task<bool> ExistePendenteAsync(Guid empresaId) =>
        dbContext.CobrancasAssinatura
            .AnyAsync(c => c.EmpresaId == empresaId && c.Status == StatusCobranca.Pendente);

    public async Task<IEnumerable<CobrancaAssinatura>> GetByEmpresaAsync(Guid empresaId, int limit = 24) =>
        await dbContext.CobrancasAssinatura
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .OrderByDescending(c => c.CriadoEm)
            .Take(limit)
            .ToListAsync();

    public async Task<IEnumerable<CobrancaAssinatura>> GetPendentesParaDunningAsync(CancellationToken ct = default)
    {
        // Cobranças pendentes de assinaturas suspensas — candidatas para dunning.
        return await dbContext.CobrancasAssinatura
            .Include(c => c.Assinatura)
            .Where(c => c.Status == StatusCobranca.Pendente
                     && c.Assinatura != null
                     && c.Assinatura.Status == StatusAssinatura.Suspensa
                     && c.TentativasLembrete < 4)
            .ToListAsync(ct);
    }
}
