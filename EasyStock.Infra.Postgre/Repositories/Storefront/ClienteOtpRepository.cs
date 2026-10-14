using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class ClienteOtpRepository(EasyStockDbContext db) : IClienteOtpRepository
{
    public Task<ClienteOtp?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ClienteOtps.FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<ClienteOtp?> GetAtivoPorTelefoneHashAsync(
        Guid empresaId,
        string telefoneHash,
        DateTime now,
        CancellationToken ct = default) =>
        db.ClienteOtps
            .Where(o => o.EmpresaId == empresaId
                && o.TelefoneHash == telefoneHash
                && !o.Consumido
                && o.ExpiraEm > now)
            .OrderByDescending(o => o.CriadoEm)
            .FirstOrDefaultAsync(ct);

    public Task<int> ContarCriadosDesdeAsync(
        Guid empresaId,
        string telefoneHash,
        DateTime desde,
        CancellationToken ct = default) =>
        db.ClienteOtps
            .Where(o => o.EmpresaId == empresaId
                && o.TelefoneHash == telefoneHash
                && o.CriadoEm >= desde)
            .CountAsync(ct);

    public Task AddAsync(ClienteOtp otp, CancellationToken ct = default)
    {
        db.ClienteOtps.Add(otp);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ClienteOtp otp, CancellationToken ct = default)
    {
        db.ClienteOtps.Update(otp);
        return Task.CompletedTask;
    }

    public async Task<bool> ReservarTentativaAsync(Guid otpId, CancellationToken ct = default) =>
        // #1781: limite no WHERE de um UPDATE só; paralelas se enfileiram na trava da linha.
        await db.ClienteOtps
            .Where(o => o.Id == otpId && !o.Consumido && o.Tentativas < ClienteOtp.MaxTentativas)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Tentativas, o => o.Tentativas + 1), ct) == 1;

    public async Task<bool> ConsumirAsync(Guid otpId, CancellationToken ct = default) =>
        // #1781: o NOT Consumido no WHERE decide quem leva o OTP; a outra requisição altera 0 linhas.
        await db.ClienteOtps
            .Where(o => o.Id == otpId && !o.Consumido)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Consumido, true), ct) == 1;
}
