using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IPedidoFornecedorRepository
{
    Task<PedidoFornecedor?> GetByIdAsync(Guid id);
    Task<PedidoFornecedor?> GetByIdComItensAsync(Guid id);
    Task<(IEnumerable<PedidoFornecedor> Pedidos, int Total)> GetPedidosPaginadosAsync(
        Guid empresaId,
        Guid? fornecedorId = null,
        StatusPedidoFornecedor? status = null,
        int page = 1,
        int pageSize = 20);
    Task AddAsync(PedidoFornecedor pedido);
    Task UpdateAsync(PedidoFornecedor pedido);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetHistoricoPorFornecedorAsync(Guid empresaId, Guid fornecedorId);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosAtrasadosAsync(Guid empresaId, DateTime referencia);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosRecebidosNoPeriodoAsync(Guid empresaId, DateTime de, DateTime ate);
    Task<int> CountPedidosAbertosOuEmTransitoAsync(Guid empresaId, Guid fornecedorId);
    Task<(int QuantidadePedidos, decimal TotalGasto, decimal? LeadTimeRealMedioDias, decimal FrequenciaPedidosPorMes)> GetEstatisticasAsync(Guid empresaId, Guid fornecedorId);
}

