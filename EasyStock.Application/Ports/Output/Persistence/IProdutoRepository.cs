using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoRepository
    {
        Task<Produto?> GetByIdAsync(Guid id);
        Task<Produto?> GetByIdAsync(Guid empresaId, Guid id);
        Task<Produto?> GetDetalheAsync(Guid empresaId, Guid id);
        Task<bool> ExistsSkuBaseAsync(Guid empresaId, string skuBase, Guid? ignoreProdutoId = null);
        Task<bool> ExistsCodigoBarrasAsync(Guid empresaId, string codigoBarras, Guid? ignoreProdutoId = null);
        Task<bool> ExistsNomeAsync(Guid empresaId, string nome, Guid? ignoreProdutoId = null);
        Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo, int maxResults = 100);

        /// <summary>
        /// Listagem paginada com filtros opcionais server-side.
        /// </summary>
        /// <param name="status">Se informado, filtra apenas produtos com esse <see cref="StatusProduto"/>.</param>
        /// <param name="semPreco">Quando true, retorna apenas produtos sem PrecoReferencia definido (ou valor zero).</param>
        /// <param name="categoriaId">Se informado, filtra pela categoria.</param>
        Task<(IEnumerable<Produto> Produtos, int TotalCount)> GetProdutosPaginadosAsync(
            Guid empresaId,
            int page = 1,
            int pageSize = 20,
            string? sort = "nome",
            string? order = "asc",
            StatusProduto? status = null,
            bool semPreco = false,
            Guid? categoriaId = null);

        Task InsertAsync(Produto produto);
        Task UpdateAsync(Produto produto);
        Task<IReadOnlyList<string>> GetMarcasAsync(Guid empresaId, string? filtro = null, int max = 20);
        Task<int> CountByEmpresaAsync(Guid empresaId);

        /// <summary>
        /// Retorna o <see cref="TipoEmbalagem"/> dos produtos informados em uma unica query.
        /// Usado por CriarLote/FinalizarLote/SyncController.ApplyBatch para validar
        /// se peso e obrigatorio (RDC 727/2022 - peso so obrigatorio para Embalado).
        /// Inserido 2026-05-16 (correcao C2).
        /// </summary>
        Task<IReadOnlyDictionary<Guid, TipoEmbalagem>> GetTipoEmbalagemMapAsync(
            Guid empresaId, IEnumerable<Guid> produtoIds);
    }
}
