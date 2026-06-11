namespace EasyStock.Application.Ports.Output.Persistence;

public interface IPreferenciaMenuRepository
{
    /// <summary>Preferencia do usuario para a loja, ou null se nunca personalizou.</summary>
    Task<PreferenciaMenuUsuario?> GetAsync(Guid usuarioId, Guid lojaId);
    Task AddAsync(PreferenciaMenuUsuario preferencia);
    Task UpdateAsync(PreferenciaMenuUsuario preferencia);
}
