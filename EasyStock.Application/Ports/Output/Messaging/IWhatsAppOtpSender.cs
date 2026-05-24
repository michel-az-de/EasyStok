namespace EasyStock.Application.Ports.Output.Messaging;

/// <summary>
/// Porta para envio de código OTP via WhatsApp/SMS. Implementação concreta vive em
/// <c>EasyStock.Infra.Integrations</c> (real provider — WhatsApp Cloud API) ou
/// stub (Development — apenas loga código no console).
///
/// <para>
/// Use cases recebem essa interface — nunca chamam o provider diretamente,
/// preservando Clean Architecture e permitindo swap stub↔real conforme env.
/// </para>
///
/// <para>
/// <strong>Idempotência</strong>: <see cref="EnviarOtpAsync"/> NÃO é idempotente.
/// Cada chamada dispara uma nova mensagem. Idempotência fica a cargo do use case
/// (janela de 60s no <c>SolicitarOtpUseCase</c> evita double-send em double-tap).
/// </para>
///
/// <para>
/// <strong>Falhas</strong>: implementações devem lançar
/// <c>EasyStock.Domain.Exceptions.Storefront.OtpProviderException</c> em qualquer
/// falha upstream (timeout, 4xx/5xx do provider, etc.) — caller mapeia para HTTP 502.
/// </para>
/// </summary>
public interface IWhatsAppOtpSender
{
    /// <summary>
    /// Dispara o envio do código <paramref name="codigo"/> para
    /// <paramref name="telefoneE164"/> (formato +55XXXXXXXXXXX já validado).
    /// </summary>
    Task EnviarOtpAsync(
        string telefoneE164,
        string codigo,
        CancellationToken ct = default);
}
