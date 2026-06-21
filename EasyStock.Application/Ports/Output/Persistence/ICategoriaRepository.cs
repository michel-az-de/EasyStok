namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface ICategoriaRepository
    {
        Task<Categoria?> GetByIdAsync(Guid id);
        Task<Categoria?> GetByIdAsync(Guid empresaId, Guid id);
        Task<IEnumerable<Categoria>> GetByEmpresaAsync(Guid empresaId);
        Task<bool> ExisteProdutosNaCategoriaAsync(Guid categoriaId);
        /// <summary>BUG-08: true se já existe categoria com esse nome (case-insensitive) na
        /// empresa. <paramref name="ignorarId"/> exclui a própria categoria no update.</summary>
        Task<bool> ExisteNomeAsync(Guid empresaId, string nome, Guid? ignorarId = null);
        Task AddAsync(Categoria categoria);
        Task UpdateAsync(Categoria categoria);
        Task DeleteAsync(Guid empresaId, Guid id);
    }
}
