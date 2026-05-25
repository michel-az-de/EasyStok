using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public class ResetTokenRepository(EasyStockDbContext context) : IResetTokenRepository
{
    private readonly EasyStockDbContext _context = context;

    public Task<ResetToken?> GetByTokenAsync(string token)
    {
        var hash = TokenHashHelper.ComputeSha256Hash(token);
        return _context.ResetTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);
    }

    public async Task<IEnumerable<ResetToken>> GetByUsuarioIdAsync(Guid usuarioId) =>
        await _context.ResetTokens.Where(rt => rt.UsuarioId == usuarioId).ToListAsync();

    public async Task AddAsync(ResetToken resetToken)
    {
        await _context.ResetTokens.AddAsync(resetToken);
    }

    public Task UpdateAsync(ResetToken resetToken)
    {
        _context.ResetTokens.Update(resetToken);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        var token = _context.ResetTokens.Find(id);
        if (token != null)
            _context.ResetTokens.Remove(token);
        return Task.CompletedTask;
    }

    public async Task DeleteExpiredAsync()
    {
        var expiredTokens = await _context.ResetTokens
            .Where(rt => rt.ExpiraEm < DateTime.UtcNow)
            .ToListAsync();
        _context.ResetTokens.RemoveRange(expiredTokens);
    }

    public Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId) =>
        _context.ResetTokens.Where(rt => rt.UsuarioId == usuarioId).ExecuteDeleteAsync();
}
