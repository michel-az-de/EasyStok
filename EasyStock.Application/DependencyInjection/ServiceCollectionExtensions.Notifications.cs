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
        services.AddSingleton<RotinaScheduler>();
        // Quarentena (N1): prazos por tipo; sobrescritas em Notifications:Quarentena:Prazos (bind em AddNotificationsCore).
        services.TryAddSingleton<PoliticaValidadeNotificacao>();
        services.AddScoped<IResolvedorAudiencia, ResolvedorAudiencia>();
        services.AddScoped<NotificadorService>();
        services.AddScoped<INotificadorService>(sp => sp.GetRequiredService<NotificadorService>());
        // N10: porta única dos avisos de problema no sistema (monitor de endpoint, snapshot de saúde, coletor de 5xx).
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPublicadorIncidenteSistema, PublicadorIncidenteSistema>();

        // Orchestrators (Avaliador e Coletor são puros — Dispatcher é registrado em Infra.Postgre)
        services.AddScoped<INotificacoesAvaliadorOrchestrator, NotificacoesAvaliadorOrchestrator>();
        services.AddScoped<INotificacoesColetorOrchestrator, NotificacoesColetorOrchestrator>();

        // Use cases — eventos
        services.AddScoped<PublicarEventoNotificacaoUseCase>();

        // WhatsApp de plataforma (N6): webhook do 2º número (status, resposta automática) e recategorização de template.
        services.AddScoped<EasyStock.Application.UseCases.Notifications.Plataforma.ProcessarStatusWhatsAppPlataformaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Notifications.Plataforma.ProcessarCategoriaTemplateWhatsAppUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Notifications.Plataforma.ResponderMensagemRecebidaPlataformaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Notifications.Plataforma.ProcessarWebhookWhatsAppPlataformaUseCase>();

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
