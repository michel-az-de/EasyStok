using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IPedidoFornecedorRepository
{
    Task<PedidoFornecedor?> GetByIdAsync(Guid id);
    Task AddAsync(PedidoFornecedor pedido);
    Task UpdateAsync(PedidoFornecedor pedido);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetHistoricoPorFornecedorAsync(Guid empresaId, Guid fornecedorId);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosAtrasadosAsync(Guid empresaId, DateTime referencia);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosRecebidosNoPeriodoAsync(Guid empresaId, DateTime de, DateTime ate);
    Task<int> CountPedidosAbertosOuEmTransitoAsync(Guid empresaId, Guid fornecedorId);
    Task<(int QuantidadePedidos, decimal TotalGasto, decimal? LeadTimeRealMedioDias, decimal FrequenciaPedidosPorMes)> GetEstatisticasAsync(Guid empresaId, Guid fornecedorId);
}
