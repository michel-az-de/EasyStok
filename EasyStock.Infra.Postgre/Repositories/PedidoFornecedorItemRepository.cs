using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Repositories
{
    public class PedidoFornecedorItemRepository : IPedidoFornecedorItemRepository
    {
        private readonly EasyStockDbContext _dbContext;
        private readonly ILogger<PedidoFornecedorItemRepository> _logger;

        public PedidoFornecedorItemRepository(
            EasyStockDbContext dbContext,
            ILogger<PedidoFornecedorItemRepository> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<IEnumerable<PedidoFornecedorItem>> GetByPedidoIdAsync(
            Guid pedidoId,
            CancellationToken ct = default)
        {
            _logger.LogDebug("Buscando itens do pedido {PedidoId}", pedidoId);

            return await _dbContext.PedidosFornecedor
                .Where(p => p.Id == pedidoId)
                .SelectMany(p => p.Itens)
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<PedidoFornecedorItem?> GetByIdAsync(
            Guid itemId,
            CancellationToken ct = default)
        {
            return await _dbContext.Set<PedidoFornecedorItem>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == itemId, ct);
        }

        public async Task InsertAsync(PedidoFornecedorItem item, CancellationToken ct = default)
        {
            await _dbContext.Set<PedidoFornecedorItem>().AddAsync(item, ct);
        }

        public async Task UpdateAsync(PedidoFornecedorItem item, CancellationToken ct = default)
        {
            _dbContext.Set<PedidoFornecedorItem>().Update(item);
            await Task.CompletedTask;
        }

        public async Task<bool> ExisteAsync(Guid itemId, CancellationToken ct = default)
        {
            return await _dbContext.Set<PedidoFornecedorItem>()
                .AnyAsync(x => x.Id == itemId, ct);
        }
    }
}
