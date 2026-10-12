namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
        Task<IEnumerable<RefreshToken>> GetByUsuarioIdAsync(Guid usuarioId);
        Task AddAsync(RefreshToken refreshToken);
        Task UpdateAsync(RefreshToken refreshToken);
        Task DeleteAsync(Guid id);
        /// <summary>Apaga em lote (<c>ExecuteDelete</c>) os refresh tokens que expiraram antes de <paramref name="expiradosAntesDe"/>, revogados ou não.</summary>
        Task<int> DeleteExpiredAsync(DateTime expiradosAntesDe, CancellationToken ct = default);
        Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId);
        Task<int> RevogarSessoesAtivasAsync(Guid usuarioId, DateTime agora);
    }
}