using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Storefront.Auth;

/// <summary>
/// Use case do primeiro passo do fluxo de autenticação storefront (ADR-0012):
/// cliente fornece telefone → gera código 6 dígitos numérico → persiste hash
/// (<c>BCrypt</c>) em <c>ClienteOtp</c> → dispara WhatsApp via
/// <see cref="IWhatsAppOtpSender"/>.
///
/// <para>
/// <strong>Anti-abuso</strong>: máx 3 OTPs/hora/telefone (4ª = 429 com
/// <see cref="OtpRateLimitExcedidoException"/>). 5 tentativas/OTP e
/// expiração 5 min ficam por conta da entity <c>ClienteOtp</c> (EZ-005).
/// </para>
///
/// <para>
/// <strong>Idempotência</strong>: janela de 60s por <c>telefoneHash</c>. Se há
/// OTP criado recentemente (independentemente da <c>IdempotencyKey</c>), retorna
/// o mesmo TTL sem regerar nem enviar — cobre double-tap do "Reenviar". Após
/// 60s, gera novo código (substitui o anterior; o repo retorna apenas o mais
/// recente em <c>GetAtivoPorTelefoneHashAsync</c>).
/// </para>
///
/// <para>
/// <strong>Logging seguro</strong>: telefone sempre mascarado
/// (<c>+5511*****1234</c>); código nunca aparece em log/response/métrica.
/// Idempotency key é logada como correlation id (não é PII).
/// </para>
///
/// <para>
/// <strong>Não inclui</strong>: validar OTP (EZ-AUTH-002) + criar
/// <c>ClienteSession</c>.
/// </para>
/// </summary>
public sealed class SolicitarOtpUseCase(
    IStorefrontRepository storefrontRepository,
    IClienteOtpRepository clienteOtpRepository,
    IWhatsAppOtpSender whatsAppOtpSender,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<SolicitarOtpUseCase> logger)
{
    public Task<SolicitarOtpResult> ExecuteAsync(SolicitarOtpInput input) =>
        throw new NotImplementedException(
            "EZ-AUTH-001 RED stage — implementação vem no próximo commit.");
}
