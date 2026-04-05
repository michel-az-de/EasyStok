using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class PedidoFornecedorRepository(EasyStockDbContext dbContext) : IPedidoFornecedorRepository
{
    public Task<PedidoFornecedor?> GetByIdAsync(Guid id) =>
        dbContext.PedidosFornecedor.FirstOrDefaultAsync(x => x.Id == id);

    public Task<PedidoFornecedor?> GetByIdComItensAsync(Guid id) =>
        dbContext.PedidosFornecedor
            .Include(x => x.Itens)
            .Include(x => x.Fornecedor)
            .FirstOrDefaultAsync(x => x.Id == id);

    public async Task<(IEnumerable<PedidoFornecedor> Pedidos, int Total)> GetPedidosPaginadosAsync(
        Guid empresaId,
        Guid? fornecedorId = null,
        StatusPedidoFornecedor? status = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = dbContext.PedidosFornecedor
            .AsNoTracking()
            .Include(x => x.Itens)
            .Include(x => x.Fornecedor)
            .Where(x => x.EmpresaId == empresaId);

        if (fornecedorId.HasValue)
            query = query.Where(x => x.FornecedorId == fornecedorId.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var total = await query.CountAsync();
        var pedidos = await query
            .OrderByDescending(x => x.DataPedido)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (pedidos, total);
    }

    public Task AddAsync(PedidoFornecedor pedido) =>
        dbContext.PedidosFornecedor.AddAsync(pedido).AsTask();

    public Task UpdateAsync(PedidoFornecedor pedido)
    {
        dbContext.PedidosFornecedor.Update(pedido);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyCollection<PedidoFornecedor>> GetHistoricoPorFornecedorAsync(Guid empresaId, Guid fornecedorId) =>
        await dbContext.PedidosFornecedor
            .AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.FornecedorId == fornecedorId)
            .OrderByDescending(x => x.DataPedido)
            .ToListAsync();

    public async Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosAtrasadosAsync(Guid empresaId, DateTime referencia) =>
        await dbContext.PedidosFornecedor
            .AsNoTracking()
            .Where(x => x.EmpresaId == empresaId &&
                        x.PrevisaoEntrega.HasValue &&
                        x.PrevisaoEntrega.Value.Date < referencia.Date &&
                        (x.Status == StatusPedidoFornecedor.Aberto || x.Status == StatusPedidoFornecedor.EmTransito))
            .ToListAsync();

    public async Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosRecebidosNoPeriodoAsync(Guid empresaId, DateTime de, DateTime ate) =>
        await dbContext.PedidosFornecedor
            .AsNoTracking()
            .Where(x => x.EmpresaId == empresaId &&
                        x.Status == StatusPedidoFornecedor.Recebido &&
                        x.DataRecebimento.HasValue &&
                        x.DataRecebimento.Value >= de &&
                        x.DataRecebimento.Value <= ate)
            .ToListAsync();

    public Task<int> CountPedidosAbertosOuEmTransitoAsync(Guid empresaId, Guid fornecedorId) =>
        dbContext.PedidosFornecedor.CountAsync(x =>
            x.EmpresaId == empresaId &&
            x.FornecedorId == fornecedorId &&
            (x.Status == StatusPedidoFornecedor.Aberto || x.Status == StatusPedidoFornecedor.EmTransito));

    public async Task<(int QuantidadePedidos, decimal TotalGasto, decimal? LeadTimeRealMedioDias, decimal FrequenciaPedidosPorMes)> GetEstatisticasAsync(Guid empresaId, Guid fornecedorId)
    {
        var pedidos = await dbContext.PedidosFornecedor
            .AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.FornecedorId == fornecedorId)
            .ToListAsync();

        if (pedidos.Count == 0)
            return (0, 0m, null, 0m);

        var totalGasto = pedidos
            .Where(x => x.Status != StatusPedidoFornecedor.Cancelado)
            .Sum(x => x.ValorEstimado ?? 0m);

        var leadTimes = pedidos
            .Where(x => x.DataRecebimento.HasValue && x.DataRecebimento.Value >= x.DataPedido)
            .Select(x => (decimal)(x.DataRecebimento!.Value.Date - x.DataPedido.Date).TotalDays)
            .ToList();

        var primeiroPedido = pedidos.Min(x => x.DataPedido.Date);
        var ultimoMarco = pedidos.Max(x => (x.DataRecebimento ?? x.DataPedido).Date);
        var meses = Math.Max(1m, (decimal)(ultimoMarco - primeiroPedido).TotalDays / 30m);
        var frequencia = decimal.Round(pedidos.Count / meses, 2);

        return (
            pedidos.Count,
            totalGasto,
            leadTimes.Count == 0 ? null : decimal.Round(leadTimes.Average(), 2),
            frequencia);
    }
}
