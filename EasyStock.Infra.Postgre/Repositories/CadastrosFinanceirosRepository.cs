using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class CadastrosFinanceirosRepository(EasyStockDbContext db) : ICadastrosFinanceirosRepository
{
    public async Task<IReadOnlyList<ContaBancaria>> ListarContasAsync(Guid empresaId, CancellationToken ct)
        => await db.Set<ContaBancaria>().AsNoTracking().Where(c => c.EmpresaId == empresaId).OrderBy(c => c.Nome).ToListAsync(ct);
    public Task<ContaBancaria?> ObterContaAsync(Guid empresaId, Guid id, CancellationToken ct)
        => db.Set<ContaBancaria>().SingleOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);
    public void Adicionar(ContaBancaria conta) => db.Set<ContaBancaria>().Add(conta);
    public async Task<IReadOnlyList<MetodoPagamentoEmpresa>> ListarMetodosAsync(Guid empresaId, CancellationToken ct)
        => await db.Set<MetodoPagamentoEmpresa>().AsNoTracking().Where(c => c.EmpresaId == empresaId).ToListAsync(ct);
    public Task<MetodoPagamentoEmpresa?> ObterMetodoAsync(Guid empresaId, string codigo, CancellationToken ct)
        => db.Set<MetodoPagamentoEmpresa>().SingleOrDefaultAsync(c => c.EmpresaId == empresaId && c.Codigo == codigo, ct);
    public void Adicionar(MetodoPagamentoEmpresa metodo) => db.Set<MetodoPagamentoEmpresa>().Add(metodo);
}
