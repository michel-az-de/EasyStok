using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.UseCases.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockNotificationsUseCases(
        this IServiceCollection services)
    {
        // Services
        services.AddScoped<ResolvedorCanal>();
        // Quarentena (N1): prazos por tipo; sobrescritas em Notifications:Quarentena:Prazos (bind em AddNotificationsCore).
        services.TryAddSingleton<PoliticaValidadeNotificacao>();
        services.AddScoped<IResolvedorAudiencia, ResolvedorAudiencia>();
        services.AddScoped<NotificadorService>();
        services.AddScoped<INotificadorService>(sp => sp.GetRequiredService<NotificadorService>());
        // N10: porta única dos avisos de problema no sistema (monitor de endpoint, snapshot de saúde, coletor de 5xx).
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPublicadorIncidenteSistema, PublicadorIncidenteSistema>();

        // Rotinas agendadas (N12): um construtor de payload por tipo agendado; o coletor (Infra.Postgre) é genérico.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConstrutorPayloadAgendado, ConstrutorPayloadResumoDiario>());

        // Orchestrators (Avaliador e Coletor são puros — Dispatcher é registrado em Infra.Postgre)
        services.AddScoped<INotificacoesAvaliadorOrchestrator, NotificacoesAvaliadorOrchestrator>();
        services.AddScoped<INotificacoesColetorOrchestrator, NotificacoesColetorOrchestrator>();

        // Use cases — eventos
        services.AddScoped<PublicarEventoNotificacaoUseCase>();

        // Use cases — consentimento
        services.AddScoped<RegistrarOptInUseCase>();
        services.AddScoped<RegistrarOptOutUseCase>();

        // Use cases — templates
        services.AddScoped<CriarTemplateUseCase>();
        services.AddScoped<AtualizarTemplateUseCase>();
        services.AddScoped<AprovarTemplateUseCase>();
        services.AddScoped<PreviewTemplateUseCase>();
        services.AddScoped<PreviewDraftTemplateUseCase>();

        // Use cases — rotinas
        services.AddScoped<CriarRotinaUseCase>();
        services.AddScoped<AtualizarRotinaUseCase>();
        services.AddScoped<AtivarRotinaUseCase>();
        services.AddScoped<DesativarRotinaUseCase>();

        // Use cases — kill switch
        services.AddScoped<AtivarKillSwitchUseCase>();
        services.AddScoped<RemoverKillSwitchUseCase>();

        // Disparo de teste por tipo (N13) e empresa padrao da plataforma (extraida do AuthController)
        services.TryAddSingleton<EmpresaPadraoCache>();
        services.TryAddScoped<IEmpresaPadraoResolver, EmpresaPadraoResolver>();
        services.TryAddSingleton(sp => new LimitadorDisparoTeste(sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddScoped<DispararTesteNotificacaoUseCase>();

        // Queries
        services.AddScoped<ListarLogsEnvioUseCase>();

        return services;
    }
}
