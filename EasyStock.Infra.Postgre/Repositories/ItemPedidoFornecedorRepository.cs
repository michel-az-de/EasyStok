using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class ItemPedidoFornecedorRepository(EasyStockDbContext dbContext) : IItemPedidoFornecedorRepository
{
    public async Task<IReadOnlyCollection<ItemPedidoFornecedor>> GetByPedidoAsync(Guid pedidoFornecedorId) =>
        await dbContext.ItensPedidoFornecedor
            .AsNoTracking()
            .Where(x => x.PedidoFornecedorId == pedidoFornecedorId)
            .ToListAsync();

    public async Task AddRangeAsync(IEnumerable<ItemPedidoFornecedor> itens) =>
        await dbContext.ItensPedidoFornecedor.AddRangeAsync(itens);

    public async Task RemoveByPedidoAsync(Guid pedidoFornecedorId)
    {
        var itens = await dbContext.ItensPedidoFornecedor
            .Where(x => x.PedidoFornecedorId == pedidoFornecedorId)
            .ToListAsync();

        if (itens.Count > 0)
            dbContext.ItensPedidoFornecedor.RemoveRange(itens);
    }
}
