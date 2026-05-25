using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IClienteOtpRepository
{
    Task<ClienteOtp?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Retorna o OTP ativo (não consumido, não expirado) mais recente para o telefone hash.</summary>
    Task<ClienteOtp?> GetAtivoPorTelefoneHashAsync(
        Guid empresaId,
        string telefoneHash,
        DateTime now,
        CancellationToken ct = default);

    Task AddAsync(ClienteOtp otp, CancellationToken ct = default);
    Task UpdateAsync(ClienteOtp otp, CancellationToken ct = default);
}
