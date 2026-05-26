using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Implementação stub de <see cref="IWhatsAppOtpSender"/> para Development.
///
/// <para>
/// <strong>Comportamento</strong>: loga o código OTP em <c>LogDebug</c> (apenas
/// em Development — produção bloqueia). Permite testar o fluxo de autenticação
/// end-to-end sem custo de SMS/WhatsApp real e sem dependência de Meta Business
/// Verification (TASK-HUM-001).
/// </para>
///
/// <para>
/// <strong>Segurança</strong>: o stub NUNCA roda em Production — checa
/// <see cref="IHostEnvironment"/> no construtor e lança
/// <see cref="OtpProviderException"/> caso alguém o registre por engano lá.
/// O DI é cuidadoso em registrá-lo só em Development (ver
/// <c>IntegrationsServiceCollectionExtensions.AddEasyStockWhatsAppStub</c>),
/// mas defense-in-depth.
/// </para>
///
/// <para>
/// <strong>Substituir por provider real</strong>: TASK-EZ-WA-001 implementará
/// um <c>WhatsAppCloudApiOtpSender</c> usando a Cloud API da Meta. Mesma
/// interface, mesma DI extension.
/// </para>
/// </summary>
public sealed class StubWhatsAppOtpSender : IWhatsAppOtpSender
{
    private readonly ILogger<StubWhatsAppOtpSender> _logger;

    public StubWhatsAppOtpSender(
        IHostEnvironment hostEnvironment,
        ILogger<StubWhatsAppOtpSender> logger)
    {
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(logger);

        if (hostEnvironment.IsProduction())
        {
            throw new OtpProviderException(
                "StubWhatsAppOtpSender NÃO pode rodar em Production. " +
                "Registre WhatsAppCloudApiOtpSender (TASK-EZ-WA-001) no DI.");
        }

        _logger = logger;
    }

    public Task EnviarOtpAsync(string telefoneE164, string codigo, CancellationToken ct = default)
    {
        // LogDebug deliberado: em Development a saída é visível no console;
        // qualquer environment acima (Staging/Production) costuma rodar em
        // Information ou superior, então o código não escapa para logs
        // persistidos. Ainda assim, o stub só roda em ambiente que NÃO é
        // Production (guarda no ctor).
        _logger.LogDebug(
            "[STUB-WA] Enviando OTP. telefone={Telefone} codigo={Codigo}",
            telefoneE164, codigo);
        return Task.CompletedTask;
    }
}
