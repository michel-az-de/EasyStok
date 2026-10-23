using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

/// <summary>
/// Port storefront-específico para o <see cref="ClienteEntity"/> ERP.
/// Expõe somente as operações necessárias para o fluxo de autenticação OTP
/// (AUTH-002) — lookup por TelefoneHash, criação e atualização de acesso.
/// </summary>
public interface IClienteStorefrontRepository
{
    /// <summary>Retorna o cliente pelo SHA-256 do telefone E.164 dentro da empresa.</summary>
    Task<ClienteEntity?> GetByTelefoneHashAsync(Guid empresaId, string telefoneHash, CancellationToken ct = default);

    /// <summary>
    /// PEDIDO-06: cliente BLOQUEADO da empresa com este telefone (principal ou extra), em qualquer cadastro: o do
    /// WhatsApp, o do OTP ou o de um pedido guest anterior. O bloqueio vale pelo telefone, não pelo hash.
    /// </summary>
    Task<ClienteEntity?> GetBloqueadoPorTelefoneAsync(Guid empresaId, string telefoneE164, CancellationToken ct = default);

    Task<ClienteEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ClienteEntity cliente, CancellationToken ct = default);
    Task UpdateAsync(ClienteEntity cliente, CancellationToken ct = default);
}
