using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface ILoteRepository
    {
        Task<Lote?> GetByIdAsync(Guid empresaId, Guid id);
        Task<Lote?> GetByIdWithDetailsAsync(Guid empresaId, Guid id);
        Task<Lote?> FindByCodigoAsync(Guid empresaId, string codigo);
        Task<Lote?> FindByMobileBatchIdAsync(Guid empresaId, string mobileBatchId);

        Task<(IEnumerable<Lote> items, int total)> ListAsync(
            Guid empresaId,
            int page,
            int pageSize,
            string? status = null,
            DateTime? desde = null,
            DateTime? ate = null,
            string? search = null,
            string? sort = "dataproducao",
            string? order = "desc");

        Task<int> GetNextSequencialDoDiaAsync(Guid empresaId, DateOnly data);

        Task AddAsync(Lote lote);
        Task UpdateAsync(Lote lote);

        Task AddItemAsync(LoteItem item);
        Task RemoveItemAsync(Guid itemId);

        Task AddEtiquetaAsync(LoteEtiqueta etiqueta);
        Task<LoteEtiqueta?> FindEtiquetaPorCodigoAsync(Guid empresaId, string codigo);
        Task UpdateEtiquetaAsync(LoteEtiqueta etiqueta);
    }
}
