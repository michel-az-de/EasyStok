namespace EasyStock.Application.Ports.Output.Persistence;

public interface IPedidoFornecedorRepository
{
    Task<PedidoFornecedor?> GetByIdAsync(Guid id);
    Task AddAsync(PedidoFornecedor pedido);

    /// <summary>Adiciona um item a um PedidoFornecedor existente (rastreado pelo DbContext).</summary>
    Task AddItemAsync(PedidoFornecedorItem item);

    Task UpdateAsync(PedidoFornecedor pedido);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetHistoricoPorFornecedorAsync(Guid empresaId, Guid fornecedorId);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosAtrasadosAsync(Guid empresaId, DateTime referencia);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosRecebidosNoPeriodoAsync(Guid empresaId, DateTime de, DateTime ate);
    Task<int> CountPedidosAbertosOuEmTransitoAsync(Guid empresaId, Guid fornecedorId);
    Task<(int QuantidadePedidos, decimal TotalGasto, decimal? LeadTimeRealMedioDias, decimal FrequenciaPedidosPorMes)> GetEstatisticasAsync(Guid empresaId, Guid fornecedorId);
    Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosAbertosComFornecedorAsync(Guid empresaId);
    Task<IEnumerable<PedidoFornecedor>> SearchAsync(Guid empresaId, string termo, int maxResults = 20);
}
