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

    public Task AddAsync(PedidoFornecedor pedido) =>
        dbContext.PedidosFornecedor.AddAsync(pedido).AsTask();

    public Task AddItemAsync(PedidoFornecedorItem item)
    {
        dbContext.Set<PedidoFornecedorItem>().Add(item);
        return Task.CompletedTask;
    }

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
                        (x.Status == StatusPedidoFornecedor.Recebido
                         || x.Status == StatusPedidoFornecedor.RecebidoParcial) &&
                        x.DataRecebimento.HasValue &&
                        x.DataRecebimento.Value >= de &&
                        x.DataRecebimento.Value <= ate)
            .ToListAsync();

    public Task<int> CountPedidosAbertosOuEmTransitoAsync(Guid empresaId, Guid fornecedorId) =>
        dbContext.PedidosFornecedor.CountAsync(x =>
            x.EmpresaId == empresaId &&
            x.FornecedorId == fornecedorId &&
            (x.Status == StatusPedidoFornecedor.Aberto || x.Status == StatusPedidoFornecedor.EmTransito));

    public async Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosAbertosComFornecedorAsync(Guid empresaId) =>
        await dbContext.PedidosFornecedor
            .AsNoTracking()
            .Include(x => x.Fornecedor)
            .Where(x => x.EmpresaId == empresaId &&
                        (x.Status == StatusPedidoFornecedor.Aberto || x.Status == StatusPedidoFornecedor.EmTransito))
            .OrderByDescending(x => x.DataPedido)
            .ToListAsync();

    public async Task<(int QuantidadePedidos, decimal TotalGasto, decimal? LeadTimeRealMedioDias, decimal FrequenciaPedidosPorMes)> GetEstatisticasAsync(Guid empresaId, Guid fornecedorId)
    {
        // Otimização: projetar apenas os campos necessários para os cálculos,
        // evitando carregar a entidade completa com todas as relações.
        // Os cálculos (média de leadtime, min/max, contagem) poderiam ir 100%
        // ao SQL mas o provider Npgsql não traduz algumas operações de DateTime,
        // então fazemos uma projeção enxuta e agregamos em memória.
        var baseQuery = dbContext.PedidosFornecedor
            .AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.FornecedorId == fornecedorId);

        var projecao = await baseQuery
            .Select(x => new
            {
                x.DataPedido,
                x.DataRecebimento,
                x.ValorEstimado,
                x.Status
            })
            .ToListAsync();

        if (projecao.Count == 0)
            return (0, 0m, null, 0m);

        var totalGasto = projecao
            .Where(x => x.Status != StatusPedidoFornecedor.Cancelado)
            .Sum(x => x.ValorEstimado ?? 0m);

        var leadTimes = projecao
            .Where(x => x.DataRecebimento.HasValue && x.DataRecebimento.Value >= x.DataPedido)
            .Select(x => (decimal)(x.DataRecebimento!.Value.Date - x.DataPedido.Date).TotalDays)
            .ToList();

        var primeiroPedido = projecao.Min(x => x.DataPedido.Date);
        var ultimoMarco = projecao.Max(x => (x.DataRecebimento ?? x.DataPedido).Date);
        var meses = Math.Max(1m, (decimal)(ultimoMarco - primeiroPedido).TotalDays / 30m);
        var frequencia = decimal.Round(projecao.Count / meses, 2);

        return (
            projecao.Count,
            totalGasto,
            leadTimes.Count == 0 ? null : decimal.Round(leadTimes.Average(), 2),
            frequencia);
    }

    public async Task<IEnumerable<PedidoFornecedor>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
    {
        var pattern = $"%{termo.Trim()}%";
        return await dbContext.PedidosFornecedor
            .AsNoTracking()
            .Include(p => p.Fornecedor)
            .Where(p => p.EmpresaId == empresaId &&
                ((p.Observacoes != null && EF.Functions.ILike(p.Observacoes, pattern)) ||
                 (p.Tracking != null && EF.Functions.ILike(p.Tracking, pattern)) ||
                 (p.Canal != null && EF.Functions.ILike(p.Canal, pattern)) ||
                 (p.Fornecedor != null && EF.Functions.ILike(p.Fornecedor.Nome, pattern))))
            .OrderByDescending(p => p.DataPedido)
            .Take(maxResults)
            .ToListAsync();
    }
}
