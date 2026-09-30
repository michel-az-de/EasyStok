using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>CRM leve do cliente (S24). <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).</summary>
public sealed class ClienteCrmRepository(EasyStockDbContext db) : IClienteCrmRepository
{
    public Task<Cliente?> ObterComTagsAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default) =>
        db.Clientes
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == clienteId, ct);

    public async Task<IReadOnlyList<ClienteNota>> ListarNotasAsync(Guid empresaId, Guid clienteId, int maximo, CancellationToken ct = default) =>
        await db.ClienteNotas
            .AsNoTracking()
            .Where(n => n.EmpresaId == empresaId && n.ClienteId == clienteId)
            .OrderByDescending(n => n.CriadoEm)
            .Take(maximo)
            .ToListAsync(ct);

    public Task AdicionarNotaAsync(ClienteNota nota, CancellationToken ct = default) =>
        db.ClienteNotas.AddAsync(nota, ct).AsTask();

    public Task<bool> PedidoEhDoClienteAsync(Guid empresaId, Guid pedidoId, Guid clienteId, CancellationToken ct = default) =>
        db.Pedidos.AnyAsync(p => p.EmpresaId == empresaId && p.Id == pedidoId && p.ClienteId == clienteId, ct);
}
