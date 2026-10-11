namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IEmailConfirmationTokenRepository
    {
        Task<EmailConfirmationToken?> GetByTokenAsync(string token);
        Task<IEnumerable<EmailConfirmationToken>> GetByUsuarioIdAsync(Guid usuarioId);
        Task AddAsync(EmailConfirmationToken token);
        Task UpdateAsync(EmailConfirmationToken token);
        Task DeleteAsync(Guid id);
        /// <summary>Apaga em lote (<c>ExecuteDelete</c>) os tokens de confirmação que expiraram antes de <paramref name="expiradosAntesDe"/>, confirmados ou não.</summary>
        Task<int> DeleteExpiredAsync(DateTime expiradosAntesDe, CancellationToken ct = default);
        Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId);
    }
}
