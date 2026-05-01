using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IEmailConfirmationTokenRepository
    {
        Task<EmailConfirmationToken?> GetByTokenAsync(string token);
        Task<IEnumerable<EmailConfirmationToken>> GetByUsuarioIdAsync(Guid usuarioId);
        Task AddAsync(EmailConfirmationToken token);
        Task UpdateAsync(EmailConfirmationToken token);
        Task DeleteAsync(Guid id);
        Task DeleteExpiredAsync();
    }
}
