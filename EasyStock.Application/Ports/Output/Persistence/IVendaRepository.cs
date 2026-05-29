namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IVendaRepository
    {
        Task<Venda?> GetByIdAsync(Guid id);
        Task<Venda?> GetByIdAsync(Guid empresaId, Guid id);
        Task<(IEnumerable<Venda> Vendas, int TotalCount)> GetVendasPorEmpresaAsync(Guid empresaId, int page = 1, int pageSize = 20);
        Task InsertAsync(Venda venda);
    }
}
