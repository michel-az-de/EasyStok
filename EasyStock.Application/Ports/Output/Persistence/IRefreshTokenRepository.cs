using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
        Task<IEnumerable<RefreshToken>> GetByUsuarioIdAsync(Guid usuarioId);
        Task AddAsync(RefreshToken refreshToken);
        Task UpdateAsync(RefreshToken refreshToken);
        Task DeleteAsync(Guid id);
        Task DeleteExpiredAsync();
        Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId);
    }
}