using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>Sem provider real configurado, a autenticação informa indisponibilidade sem simular envio.</summary>
public sealed class IndisponivelWhatsAppOtpSender : IWhatsAppOtpSender
{
    public Task ValidarDisponibilidadeAsync(CancellationToken ct = default) =>
        throw new OtpProviderException("Envio de código indisponível: provider OTP não configurado.");

    public Task EnviarOtpAsync(string telefoneE164, string codigo, CancellationToken ct = default) =>
        ValidarDisponibilidadeAsync(ct);
}
