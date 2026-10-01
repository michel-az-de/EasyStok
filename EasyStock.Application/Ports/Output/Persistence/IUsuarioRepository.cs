namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsuarioRepository
    {
        Task<Usuario?> GetByIdAsync(Guid id);
        Task<Usuario?> GetByEmailAsync(string email);

        /// <summary>#1324: usuários com e-mail <c>{parteLocal}+…@gmail.com</c>, carregados como no login.</summary>
        Task<IReadOnlyList<Usuario>> ListarPorAliasGmailAsync(string parteLocal);
        Task<(IEnumerable<Usuario> Usuarios, int Total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize);
        Task<int> CountByEmpresaAsync(Guid empresaId);
        Task AddAsync(Usuario usuario);
        Task UpdateAsync(Usuario usuario);
        Task<IEnumerable<Usuario>> SearchAsync(Guid empresaId, string termo, int maxResults = 20);
    }
}
