using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IListaComprasRepository
    {
        Task<ListaCompras?> GetByIdAsync(Guid empresaId, Guid id);
        Task<ListaCompras?> GetByIdWithItemsAsync(Guid empresaId, Guid id);

        Task<(IEnumerable<ListaCompras> items, int total)> ListAsync(
            Guid empresaId,
            int page,
            int pageSize,
            string? status = null,
            string? search = null);

        /// <summary>Lista atualmente aberta da empresa (default behavior do app: 1 lista ativa).</summary>
        Task<ListaCompras?> GetListaAbertaAsync(Guid empresaId, Guid? lojaId = null);

        Task AddAsync(ListaCompras lista);
        Task UpdateAsync(ListaCompras lista);

        Task<ItemListaCompras?> GetItemAsync(Guid id);
        Task AddItemAsync(ItemListaCompras item);
        Task UpdateItemAsync(ItemListaCompras item);
        Task RemoveItemAsync(Guid itemId);
    }
}
