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

    /// <summary>
    /// Conta OTPs (consumidos ou não, expirados ou não) criados em <c>[desde, +∞)</c>
    /// para o par <c>(empresaId, telefoneHash)</c>. Usado pelo rate limit anti-abuso
    /// (3 OTPs/hora — 4ª chamada bloqueia).
    /// </summary>
    Task<int> ContarCriadosDesdeAsync(
        Guid empresaId,
        string telefoneHash,
        DateTime desde,
        CancellationToken ct = default);

    Task AddAsync(ClienteOtp otp, CancellationToken ct = default);
    Task UpdateAsync(ClienteOtp otp, CancellationToken ct = default);
}
