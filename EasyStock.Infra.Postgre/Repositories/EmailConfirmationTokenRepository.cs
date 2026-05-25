using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class EmailConfirmationTokenRepository(EasyStockDbContext context) : IEmailConfirmationTokenRepository
    {
        public Task<EmailConfirmationToken?> GetByTokenAsync(string token)
        {
            var hash = TokenHashHelper.ComputeSha256Hash(token);
            return context.EmailConfirmationTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TokenHash == hash);
        }

        public async Task<IEnumerable<EmailConfirmationToken>> GetByUsuarioIdAsync(Guid usuarioId) =>
            await context.EmailConfirmationTokens
                .AsNoTracking()
                .Where(t => t.UsuarioId == usuarioId)
                .ToListAsync();

        public Task AddAsync(EmailConfirmationToken token) =>
            context.EmailConfirmationTokens.AddAsync(token).AsTask();

        public Task UpdateAsync(EmailConfirmationToken token)
        {
            context.EmailConfirmationTokens.Update(token);
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(Guid id)
        {
            var token = await context.EmailConfirmationTokens.FindAsync(id);
            if (token is not null)
                context.EmailConfirmationTokens.Remove(token);
        }

        public async Task DeleteExpiredAsync()
        {
            var expired = await context.EmailConfirmationTokens
                .Where(t => t.ExpiraEm < DateTime.UtcNow)
                .ToListAsync();
            context.EmailConfirmationTokens.RemoveRange(expired);
        }

        public Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId) =>
            context.EmailConfirmationTokens.Where(t => t.UsuarioId == usuarioId).ExecuteDeleteAsync();
    }
}
