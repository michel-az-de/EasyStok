using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Async.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.DependencyInjection;

/// <summary>
/// Fabrica unica de <see cref="IEmailService"/> (N3, #1351). A API (<c>AddEasyStockAsyncInfrastructure</c>) e o Worker
/// (<c>Program.cs</c>) chamam esta mesma extensao: com a mesma configuracao resolvem o mesmo provider e as mesmas
/// opcoes. Este e o unico lugar que constroi o <see cref="SmtpEmailService"/> (o teste de arquitetura garante).
/// </summary>
public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Valida na hora de montar o container: chave invalida ou obrigatoria faltando derruba a subida
        // nomeando a chave, em vez de estourar no primeiro envio.
        var escolha = EscolhaEmail.Resolver(configuration, ResolverAmbiente(configuration));

        switch (escolha.Provider)
        {
            case EscolhaEmail.ProviderSmtp:
                var smtp = escolha.Smtp!;
                services.AddSingleton(smtp);
                services.AddSingleton<IEmailService>(sp =>
                    new SmtpEmailService(smtp, sp.GetRequiredService<ILogger<SmtpEmailService>>()));
                break;

            case EscolhaEmail.ProviderSendGrid:
                var sendGrid = escolha.SendGrid!;
                services.AddSingleton<IEmailService>(_ =>
                    new SendGridEmailService(sendGrid.ApiKey, sendGrid.FromEmail, sendGrid.FromName, sendGrid.Sandbox));
                break;

            default:
                // O NOME da classe e contrato do diagnostico ("SMTP nao configurado"): nao renomear.
                services.AddSingleton<IEmailService, ConsoleEmailService>();
                break;
        }

        services.AddSingleton(escolha);

        // #1432: caixa de suporte da loja (IMAP/SMTP por empresa), independente do provider acima, que é o da plataforma.
        services.TryAddSingleton<EasyStock.Application.Ports.Output.Atendimento.Email.ICaixaEmailCliente>(sp =>
            new EasyStock.Infra.Async.Email.Atendimento.CaixaEmailMailKit(
                sp.GetRequiredService<ILogger<EasyStock.Infra.Async.Email.Atendimento.CaixaEmailMailKit>>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EmailAvisosDeSubida>());
        return services;
    }

    // Mesma ordem do guard de Efi:UseStub (fail-safe: sem ambiente conhecido vale Production).
    private static string ResolverAmbiente(IConfiguration configuration) =>
        configuration["ASPNETCORE_ENVIRONMENT"]
        ?? configuration["DOTNET_ENVIRONMENT"]
        ?? configuration["environment"]
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? "Production";
}

/// <summary>
/// Diz na subida qual provider ficou ativo e o que a configuracao nao entregou (provider caiu no console, caixa de
/// seguranca dividindo a de avisos). Sem endereco de e-mail e sem credencial no log.
/// </summary>
internal sealed class EmailAvisosDeSubida(EscolhaEmail escolha, ILogger<EmailAvisosDeSubida> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var aviso in escolha.Avisos)
            logger.LogWarning("E-mail: {Aviso}", aviso);

        switch (escolha.Provider)
        {
            case EscolhaEmail.ProviderSmtp:
                var smtp = escolha.Smtp!;
                logger.LogInformation(
                    "E-mail: provider smtp ativo (servidor {Host}:{Porta}, transporte {Modo}, caixa de seguranca {Caixa}).",
                    smtp.Host, smtp.Porta, smtp.Modo, DescreverCaixaDeSeguranca(smtp));
                break;

            case EscolhaEmail.ProviderSendGrid:
                logger.LogInformation("E-mail: provider sendgrid ativo (remetente unico para todas as categorias).");
                break;

            default:
                logger.LogInformation("E-mail: provider console ativo, nenhum e-mail sera enviado.");
                break;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string DescreverCaixaDeSeguranca(SmtpConfiguracao smtp) =>
        smtp.SegurancaUsaAvisos ? "dividida com avisos"
        : smtp.Seguranca.ChaveBase == SmtpOpcoes.Secao ? "From proprio com a credencial de avisos"
        : "propria";
}
