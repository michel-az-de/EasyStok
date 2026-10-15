using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class CreditoClienteComandaRepository(EasyStockDbContext db) : ICreditoClienteComandaRepository
{
    public Task AddAsync(CreditoClienteComanda credito, CancellationToken ct) => db.Set<CreditoClienteComanda>().AddAsync(credito, ct).AsTask();
    public Task AdicionarUsoAsync(UsoCreditoClienteComanda uso, CancellationToken ct) => db.Set<UsoCreditoClienteComanda>().AddAsync(uso, ct).AsTask();
    public Task<CreditoClienteComanda?> ObterPorOperacaoAsync(Guid empresaId, Guid operacaoId, CancellationToken ct) =>
        db.Set<CreditoClienteComanda>().FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.OperacaoId == operacaoId, ct);
    public Task<CreditoClienteComanda?> ObterPorCodigoAsync(Guid empresaId, Guid clienteId, string codigo, CancellationToken ct) =>
        db.Set<CreditoClienteComanda>().AsNoTracking().FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.ClienteId == clienteId && c.Codigo == codigo, ct);

    public async Task<CreditoClienteComanda?> ObterComLockAsync(Guid empresaId, Guid clienteId, string codigo, CancellationToken ct)
    {
        var credito = await db.Set<CreditoClienteComanda>().FromSqlInterpolated(
            $"SELECT * FROM creditos_cliente_comanda WHERE \"EmpresaId\" = {empresaId} AND \"ClienteId\" = {clienteId} AND \"Codigo\" = {codigo} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (credito is not null) await db.Entry(credito).ReloadAsync(ct);
        return credito;
    }

    public async Task<IReadOnlyList<CreditoClienteComanda>> ListarAsync(Guid empresaId, Guid clienteId, CancellationToken ct) =>
        await db.Set<CreditoClienteComanda>().AsNoTracking().Where(c => c.EmpresaId == empresaId && c.ClienteId == clienteId && c.Saldo > 0)
            .OrderBy(c => c.CriadoEm).Take(50).ToListAsync(ct);

    public async Task<(decimal Emitido, decimal Utilizado)> TotaisDoPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct) => (
        await db.Set<CreditoClienteComanda>().Where(c => c.EmpresaId == empresaId && c.PedidoOrigemId == pedidoId).SumAsync(c => (decimal?)c.Valor, ct) ?? 0m,
        await db.Set<UsoCreditoClienteComanda>().Where(c => c.EmpresaId == empresaId && c.PedidoId == pedidoId).SumAsync(c => (decimal?)c.Valor, ct) ?? 0m);
}
