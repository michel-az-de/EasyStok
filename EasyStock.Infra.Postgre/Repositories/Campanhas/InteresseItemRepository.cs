using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Campanhas;

public sealed class InteresseItemRepository(EasyStockDbContext db) : IInteresseItemRepository
{
    public Task AddAsync(InteresseItem interesse, CancellationToken ct = default) =>
        db.InteressesItem.AddAsync(interesse, ct).AsTask();

    public Task<int> ContarAbertosDoItemAsync(Guid empresaId, Guid cardapioItemId, CancellationToken ct = default) =>
        db.InteressesItem.CountAsync(
            i => i.EmpresaId == empresaId && i.CardapioItemId == cardapioItemId && i.AtendidoEm == null, ct);

    public async Task<IReadOnlyList<InteresseAbertoCliente>> ListarAbertosDoItemAsync(
        Guid empresaId, Guid cardapioItemId, CancellationToken ct = default) =>
        await (from i in db.InteressesItem.AsNoTracking()
               join c in db.Clientes.AsNoTracking() on i.ClienteId equals c.Id
               where i.EmpresaId == empresaId && c.EmpresaId == empresaId
                     && i.CardapioItemId == cardapioItemId && i.AtendidoEm == null
               orderby i.RegistradoEm descending
               select new InteresseAbertoCliente(i.Id, c.Id, c.Nome, c.Telefone, i.Descricao, i.Origem, i.RegistradoEm))
            .ToListAsync(ct);

    public Task<InteresseItem?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.InteressesItem.FirstOrDefaultAsync(i => i.EmpresaId == empresaId && i.Id == id, ct);

    public async Task<IReadOnlyList<InteresseItem>> ListarDoClienteAsync(
        Guid empresaId, Guid clienteId, CancellationToken ct = default) =>
        await db.InteressesItem.AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && i.ClienteId == clienteId)
            .OrderByDescending(i => i.RegistradoEm)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InteresseItem>> ListarAbertosDoClienteNosItensAsync(
        Guid empresaId, Guid clienteId, IReadOnlyCollection<Guid> cardapioItemIds, CancellationToken ct = default)
    {
        if (cardapioItemIds.Count == 0) return [];
        return await db.InteressesItem
            .Where(i => i.EmpresaId == empresaId && i.ClienteId == clienteId && i.AtendidoEm == null
                        && i.CardapioItemId != null && cardapioItemIds.Contains(i.CardapioItemId.Value))
            .ToListAsync(ct);
    }
}
