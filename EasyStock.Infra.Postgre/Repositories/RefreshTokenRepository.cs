using EasyStock.Infra.Postgre.Security;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

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

    public Task<int> DeleteExpiredAsync(DateTime expiradosAntesDe, CancellationToken ct = default) =>
        _context.RefreshTokens.Where(rt => rt.ExpiraEm < expiradosAntesDe).ExecuteDeleteAsync(ct);

    public Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId) =>
        _context.RefreshTokens.Where(rt => rt.UsuarioId == usuarioId).ExecuteDeleteAsync();

    public async Task<int> RevogarSessoesAtivasAsync(Guid usuarioId, DateTime agora)
    {
        await using var rls = await _context.UseRowLevelSecurityBypassAsync();
        await _context.Set<SessaoNativa>().IgnoreQueryFilters()
            .Where(s => s.UsuarioId == usuarioId && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, agora));
        return await _context.RefreshTokens
            .Where(rt => rt.UsuarioId == usuarioId && !rt.Revogado && rt.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.Revogado, true)
                .SetProperty(rt => rt.RevogadoEm, agora));
    }

    public Task<int> RevogarSessoesWebAsync(Guid usuarioId, DateTime agora) =>
        _context.RefreshTokens.Where(rt => rt.UsuarioId == usuarioId && rt.SessaoNativaId == null && !rt.Revogado && rt.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.Revogado, true).SetProperty(rt => rt.RevogadoEm, agora));

    public Task AdicionarSessaoNativaAsync(SessaoNativa sessao)
    {
        _context.Set<SessaoNativa>().Add(sessao);
        return Task.CompletedTask;
    }

    // O refresh precede o JWT/tenant. A leitura é por segredo/ID conferido e o chamador verifica a identidade.
    public async Task<SessaoNativa?> ObterSessaoNativaAsync(Guid id)
    {
        await using var rls = await _context.UseRowLevelSecurityBypassAsync();
        return await _context.Set<SessaoNativa>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
    }

    public async Task<SessaoNativa?> ObterSessaoNativaComLockAsync(Guid id)
    {
        await using var rls = await _context.UseRowLevelSecurityBypassAsync();
        return await _context.Set<SessaoNativa>().FromSqlInterpolated($"SELECT * FROM sessoes_nativas WHERE \"Id\" = {id} FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync();
    }

    public async Task<bool> ConsumirAsync(Guid id, DateTime agora) =>
        await _context.RefreshTokens.Where(r => r.Id == id && !r.Revogado && r.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Revogado, true).SetProperty(r => r.RevogadoEm, agora)) == 1;

    public async Task RevogarSessaoNativaAsync(Guid id, DateTime agora)
    {
        await using var rls = await _context.UseRowLevelSecurityBypassAsync();
        await _context.Set<SessaoNativa>().IgnoreQueryFilters().Where(s => s.Id == id && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, agora));
        await _context.RefreshTokens.Where(r => r.SessaoNativaId == id && !r.Revogado)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Revogado, true).SetProperty(r => r.RevogadoEm, agora));
    }

    public Task<bool> DispositivoRevogadoAsync(string dispositivoId) =>
        _context.Set<EasyStock.Domain.Entities.Mobile.MobileDevice>().IgnoreQueryFilters()
            .AnyAsync(d => d.Id == dispositivoId && d.Revoked);

    public async Task TravarDispositivoAsync(string dispositivoId) =>
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM mobile_devices WHERE \"Id\" = {dispositivoId} FOR UPDATE");

    public async Task SalvarReciboRenovacaoAsync(Guid sessaoId, Guid? requisicaoId, Guid? refreshAnteriorId, string? sucessorProtegido)
    {
        await using var rls = await _context.UseRowLevelSecurityBypassAsync();
        await _context.Set<SessaoNativa>().IgnoreQueryFilters().Where(s => s.Id == sessaoId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimaRenovacaoId, requisicaoId)
                .SetProperty(x => x.RefreshAnteriorId, refreshAnteriorId).SetProperty(x => x.RefreshSucessorProtegido, sucessorProtegido));
    }
}
