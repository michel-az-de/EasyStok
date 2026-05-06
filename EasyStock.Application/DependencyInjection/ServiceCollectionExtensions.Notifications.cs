using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockNotificationsUseCases(
        this IServiceCollection services)
    {
        // Services
        services.AddScoped<ResolvedorCanal>();
        services.AddSingleton<RotinaScheduler>();
        services.AddScoped<NotificadorService>();
        services.AddScoped<INotificadorService>(sp => sp.GetRequiredService<NotificadorService>());

        // Use cases — eventos
        services.AddScoped<PublicarEventoNotificacaoUseCase>();
        services.AddScoped<EnviarNotificacaoManualUseCase>();

        // Use cases — consentimento
        services.AddScoped<RegistrarOptInUseCase>();
        services.AddScoped<RegistrarOptOutUseCase>();

        // Use cases — templates
        services.AddScoped<CriarTemplateUseCase>();
        services.AddScoped<AtualizarTemplateUseCase>();
        services.AddScoped<AprovarTemplateUseCase>();
        services.AddScoped<PreviewTemplateUseCase>();

        // Use cases — rotinas
        services.AddScoped<CriarRotinaUseCase>();
        services.AddScoped<AtualizarRotinaUseCase>();
        services.AddScoped<AtivarRotinaUseCase>();
        services.AddScoped<DesativarRotinaUseCase>();

        // Use cases — kill switch
        services.AddScoped<AtivarKillSwitchUseCase>();
        services.AddScoped<RemoverKillSwitchUseCase>();

        // Queries
        services.AddScoped<ListarLogsEnvioUseCase>();

        return services;
    }
}
