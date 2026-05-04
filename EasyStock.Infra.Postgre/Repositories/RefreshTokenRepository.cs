using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public class RefreshTokenRepository(EasyStockDbContext context) : IRefreshTokenRepository
{
    private readonly EasyStockDbContext _context = context;

    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash) =>
        _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

    public async Task<IEnumerable<RefreshToken>> GetByUsuarioIdAsync(Guid usuarioId) =>
        await _context.RefreshTokens.Where(rt => rt.UsuarioId == usuarioId).ToListAsync();

    public Task AddAsync(RefreshToken refreshToken)
    {
        _context.RefreshTokens.Add(refreshToken);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RefreshToken refreshToken)
    {
        _context.RefreshTokens.Update(refreshToken);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        var token = _context.RefreshTokens.Find(id);
        if (token != null)
            _context.RefreshTokens.Remove(token);
        return Task.CompletedTask;
    }

    public async Task DeleteExpiredAsync()
    {
        var expiredTokens = await _context.RefreshTokens
            .Where(rt => rt.ExpiraEm < DateTime.UtcNow)
            .ToListAsync();
        _context.RefreshTokens.RemoveRange(expiredTokens);
    }

    public Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId) =>
        _context.RefreshTokens.Where(rt => rt.UsuarioId == usuarioId).ExecuteDeleteAsync();

    public Task<int> RevogarSessoesAtivasAsync(Guid usuarioId, DateTime agora) =>
        _context.RefreshTokens
            .Where(rt => rt.UsuarioId == usuarioId && !rt.Revogado && rt.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.Revogado, true)
                .SetProperty(rt => rt.RevogadoEm, agora));
}