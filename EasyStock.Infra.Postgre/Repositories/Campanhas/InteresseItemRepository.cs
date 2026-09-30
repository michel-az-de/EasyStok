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
}
