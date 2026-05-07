using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Infra.Postgre.Notifications;
using EasyStock.Infra.Postgre.Notifications.Collectors;
using EasyStock.Infra.Postgre.Notifications.Dispatcher;
using EasyStock.Infra.Postgre.Repositories.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.DependencyInjection;

public static partial class ServiceCollectionExtensionsNotifications
{
    public static IServiceCollection AddEasyStockNotificationsRepositories(
        this IServiceCollection services)
    {
        services.AddScoped<ITemplateRepository, TemplateNotificacaoRepository>();
        services.AddScoped<IRotinaRepository, RotinaNotificacaoRepository>();
        services.AddScoped<IEventoNotificacaoRepository, EventoNotificacaoRepository>();
        services.AddScoped<IOutboxNotificacaoRepository, OutboxNotificacaoRepository>();
        services.AddScoped<IConsentimentoRepository, ConsentimentoRepository>();
        services.AddScoped<IConfiguracaoCanalRepository, ConfiguracaoCanalRepository>();
        services.AddScoped<IBloqueioNotificacaoRepository, BloqueioNotificacaoRepository>();
        services.AddScoped<ILogEnvioNotificacaoRepository, LogEnvioNotificacaoRepository>();
        services.AddScoped<IVariavelTemplateCatalogoRepository, VariavelTemplateCatalogoRepository>();

        // Dispatcher orchestrator — implementa também o port INotificationDispatcher (1 shard).
        // Singleton porque é stateless e cria scopes internamente via IServiceProvider.
        services.AddSingleton<NotificacoesDispatcherOrchestrator>();
        services.AddSingleton<INotificacoesDispatcherOrchestrator>(sp => sp.GetRequiredService<NotificacoesDispatcherOrchestrator>());
        services.AddSingleton<INotificationDispatcher>(sp => sp.GetRequiredService<NotificacoesDispatcherOrchestrator>());

        // Coletores de eventos de estado — vivem em Infra.Postgre porque dependem de
        // EasyStockDbContext. Worker e API ambos consomem via INotificacoesColetorOrchestrator.
        services.AddScoped<IColetorEventoNotificacao, ColetorProdutosVencendo>();
        services.AddScoped<IColetorEventoNotificacao, ColetorAssinaturasExpirando>();

        return services;
    }

    /// <summary>
    /// Registra <see cref="PostgresOutboxSignaler"/> como singleton + IHostedService.
    /// Use quando <c>Notifications:Hosting:Signaler = Postgres</c> (default).
    /// Para Polling, basta chamar <c>AddNotificationsHosting</c> em Infra.Notifications.
    /// </summary>
    public static IServiceCollection AddPostgresOutboxSignaler(this IServiceCollection services)
    {
        services.AddSingleton<PostgresOutboxSignaler>();
        services.AddSingleton<IOutboxSignaler>(sp => sp.GetRequiredService<PostgresOutboxSignaler>());
        services.AddHostedService(sp => sp.GetRequiredService<PostgresOutboxSignaler>());
        return services;
    }
}
