namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoCaracteristicaRepository
    {
        Task<IEnumerable<ProdutoCaracteristica>> GetByProdutoAsync(Guid empresaId, Guid produtoId);
        Task InsertAsync(ProdutoCaracteristica caracteristica);
        Task UpdateAsync(ProdutoCaracteristica caracteristica);
        Task DeleteAsync(Guid empresaId, Guid id);
        Task DeleteByProdutoAsync(Guid empresaId, Guid produtoId);
    }
}
