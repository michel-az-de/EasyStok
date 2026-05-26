using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

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
}
