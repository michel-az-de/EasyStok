namespace EasyStock.Application.Ports.Output.Persistence
{
    /// <summary>
    /// Acesso publico (sem multi-tenant) ao FAQ. So expoe Publicado.
    /// </summary>
    public interface IFaqRepository
    {
        Task<IReadOnlyList<FaqCategoria>> ListarCategoriasPublicasAsync(CancellationToken ct = default);
        Task<FaqItem?> ObterPorSlugAsync(string categoriaSlug, string itemSlug, CancellationToken ct = default);
        Task<(IReadOnlyList<FaqItem> Itens, int Total)> BuscarAsync(
            string? termo,
            Guid? categoriaId,
            int page,
            int pageSize,
            CancellationToken ct = default);
        Task<IReadOnlyList<FaqItem>> ListarDestaquesAsync(int top, CancellationToken ct = default);
        Task<IReadOnlyList<FaqItem>> ListarPorCategoriaAsync(Guid categoriaId, CancellationToken ct = default);
        Task RegistrarVisualizacaoAsync(FaqVisualizacao visualizacao, CancellationToken ct = default);
        Task RegistrarFeedbackAsync(FaqFeedback feedback, CancellationToken ct = default);
        Task IncrementarContadoresAsync(Guid itemId, int deltaVisualizacao, int deltaUtil, int deltaNaoUtil, CancellationToken ct = default);
    }
}
