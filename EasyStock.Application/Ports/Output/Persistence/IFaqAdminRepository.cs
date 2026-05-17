using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Persistence
{
    /// <summary>
    /// Gestao admin do FAQ. Vai sob permissao GerenciarFaq.
    /// </summary>
    public interface IFaqAdminRepository
    {
        // categorias
        Task<IReadOnlyList<FaqCategoria>> ListarCategoriasAsync(CancellationToken ct = default);
        Task<FaqCategoria?> ObterCategoriaAsync(Guid id, CancellationToken ct = default);
        Task<bool> CategoriaSlugExisteAsync(string slug, Guid? excetoId, CancellationToken ct = default);
        Task InserirCategoriaAsync(FaqCategoria categoria, CancellationToken ct = default);
        Task AtualizarCategoriaAsync(FaqCategoria categoria, CancellationToken ct = default);

        // itens
        Task<(IReadOnlyList<FaqItem> Itens, int Total)> ListarItensAsync(
            FaqStatus? status,
            Guid? categoriaId,
            string? busca,
            int page,
            int pageSize,
            CancellationToken ct = default);
        Task<FaqItem?> ObterItemAsync(Guid id, CancellationToken ct = default);
        Task<bool> ItemSlugExisteAsync(Guid categoriaId, string slug, Guid? excetoId, CancellationToken ct = default);
        Task InserirItemAsync(FaqItem item, CancellationToken ct = default);
        Task AtualizarItemAsync(FaqItem item, CancellationToken ct = default);
    }
}
