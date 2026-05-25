using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// Implementação de <see cref="IClienteStorefrontRepository"/> via EF Core.
/// Opera na tabela <c>Clientes</c> (compartilhada com o ERP) mas filtra
/// apenas pelo <c>TelefoneHash</c> — campo preenchido exclusivamente pelo
/// fluxo de autenticação storefront OTP.
/// </summary>
public sealed class ClienteStorefrontRepository(EasyStockDbContext db) : IClienteStorefrontRepository
{
    public Task<ClienteEntity?> GetByTelefoneHashAsync(
        Guid empresaId, string telefoneHash, CancellationToken ct = default) =>
        db.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.TelefoneHash == telefoneHash, ct);

    public Task<ClienteEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task AddAsync(ClienteEntity cliente, CancellationToken ct = default)
    {
        db.Clientes.Add(cliente);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ClienteEntity cliente, CancellationToken ct = default)
    {
        db.Clientes.Update(cliente);
        return Task.CompletedTask;
    }
}
