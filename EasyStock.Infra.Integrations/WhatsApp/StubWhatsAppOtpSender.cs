using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Implementação stub de <see cref="IWhatsAppOtpSender"/> que loga o código OTP
/// em vez de enviar via WhatsApp/SMS real.
///
/// <para>
/// <strong>Comportamento</strong>: por default loga em <c>LogDebug</c>
/// (visivel em Development). Em Production com <c>Otp:UseStub=true</c> opt-in
/// (issue #677), loga em <c>LogInformation</c> pra ficar visível no nível
/// padrão dos containers (sem precisar baixar o LogLevel global). Em ambos os
/// modos o código fica APENAS no log — nunca volta no response HTTP.
/// </para>
///
/// <para>
/// <strong>Segurança</strong>: o stub bloqueia em Production por default
/// (checa <see cref="IHostEnvironment"/> no construtor e lança
/// <see cref="OtpProviderException"/>). A flag <c>Otp:UseStub</c> é opt-in
/// explícito do operador — sem ela, prod fail-fast no boot. Esquema temporário
/// até TASK-EZ-WA-001 (provider real Meta Cloud API) entrar.
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
    private readonly bool _logEmInformation;

    public StubWhatsAppOtpSender(
        IHostEnvironment hostEnvironment,
        IConfiguration configuration,
        ILogger<StubWhatsAppOtpSender> logger)
    {
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var useStubOptIn = configuration.GetValue<bool>("Otp:UseStub");
        if (hostEnvironment.IsProduction() && !useStubOptIn)
        {
            throw new OtpProviderException(
                "StubWhatsAppOtpSender NÃO pode rodar em Production sem Otp:UseStub=true. " +
                "Registre WhatsAppCloudApiOtpSender (TASK-EZ-WA-001) no DI ou habilite " +
                "Otp:UseStub explicitamente (issue #677).");
        }

        // Em Production com opt-in: loga Information pra ficar visível no
        // docker logs sem precisar baixar o LogLevel global. Em Development:
        // LogDebug pra manter o console limpo (level já é Debug normalmente).
        _logEmInformation = hostEnvironment.IsProduction() && useStubOptIn;
        _logger = logger;
    }

    public Task EnviarOtpAsync(string telefoneE164, string codigo, CancellationToken ct = default)
    {
        if (_logEmInformation)
        {
            _logger.LogInformation(
                "[STUB-WA] Enviando OTP (opt-in Otp:UseStub). telefone={Telefone} codigo={Codigo}",
                telefoneE164, codigo);
        }
        else
        {
            _logger.LogDebug(
                "[STUB-WA] Enviando OTP. telefone={Telefone} codigo={Codigo}",
                telefoneE164, codigo);
        }
        return Task.CompletedTask;
    }
}
