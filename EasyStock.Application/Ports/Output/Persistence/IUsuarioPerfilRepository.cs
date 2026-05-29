namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsuarioPerfilRepository
    {
        Task AddAsync(UsuarioPerfil usuarioPerfil);
        Task<UsuarioPerfil?> GetByUsuarioEmpresaEPerfilAsync(Guid usuarioId, Guid empresaId, Guid perfilId);
        Task UpdateAsync(UsuarioPerfil usuarioPerfil);
    }
}
