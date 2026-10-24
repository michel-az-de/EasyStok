namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsuarioPerfilRepository
    {
        Task AddAsync(UsuarioPerfil usuarioPerfil);
        /// <summary>Os perfis do usuário na empresa, com o <see cref="UsuarioPerfil.Perfil"/> carregado.</summary>
        Task<IReadOnlyList<UsuarioPerfil>> ListarPorUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId);
        Task UpdateAsync(UsuarioPerfil usuarioPerfil);
        Task RemoveAsync(UsuarioPerfil usuarioPerfil);
    }
}
