namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IResetTokenRepository
    {
        Task<ResetToken?> GetByTokenAsync(string token);
        Task<IEnumerable<ResetToken>> GetByUsuarioIdAsync(Guid usuarioId);
        Task AddAsync(ResetToken resetToken);
        Task UpdateAsync(ResetToken resetToken);
        Task DeleteAsync(Guid id);
        Task DeleteExpiredAsync();
        Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId);
    }
}