using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Financeiro;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class LancamentoRepository(EasyStockDbContext db) : ILancamentoRepository
{
    public Task<Lancamento?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.Lancamentos
            .Include(l => l.Baixas)
            .FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Id == id, ct);

    public async Task<Lancamento?> GetWithLockAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        // FOR UPDATE serializa baixas concorrentes no mesmo lancamento. Exige
        // transacao explicita aberta — fora dela o lock e liberado no fim do query.
        // .IgnoreQueryFilters(): impede que o filtro global de tenant envolva o raw
        // numa subquery (que em outros padroes — ex: ItemEstoqueRepository.GetLotes
        // DisponiveisParaSaida — descarta semantica de ORDER BY/LIMIT). Aqui o raw
        // ja filtra "EmpresaId" + "Id" — isolamento de tenant preservado. Defesa
        // em profundidade contra wrap em subquery do EF.
        const string sql = "SELECT *, xmin FROM lancamentos WHERE \"EmpresaId\" = {0} AND \"Id\" = {1} FOR UPDATE";
        var lancamento = await db.Lancamentos
            .FromSqlRaw(sql, empresaId, id)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct);

        if (lancamento != null)
            await db.Entry(lancamento).Collection(l => l.Baixas).LoadAsync(ct);

        return lancamento;
    }

    public Task AddAsync(Lancamento lancamento, CancellationToken ct = default)
    {
        db.Lancamentos.Add(lancamento);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Lancamento lancamento, CancellationToken ct = default)
    {
        db.Lancamentos.Update(lancamento);
        return Task.CompletedTask;
    }
}
