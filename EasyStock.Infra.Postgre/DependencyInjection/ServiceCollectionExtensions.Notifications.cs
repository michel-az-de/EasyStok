using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Infra.Postgre.Notifications;
using EasyStock.Infra.Postgre.Notifications.Audiencia;
using EasyStock.Infra.Postgre.Notifications.Backlog;
using EasyStock.Infra.Postgre.Notifications.Collectors;
using EasyStock.Infra.Postgre.Notifications.Dispatcher;
using EasyStock.Infra.Postgre.Notifications.Maintenance;
using EasyStock.Infra.Postgre.Repositories.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Infra.Postgre.DependencyInjection;

public static partial class ServiceCollectionExtensionsNotifications
{
    /// <summary>Chave que religa o <see cref="ColetorProdutosVencendo"/> (N12): ausente ou <c>false</c>, ele não é registrado.</summary>
    public const string ProdutosVencendoHabilitadoChave = "Notifications:Coletores:ProdutosVencendo:Habilitado";

    public static IServiceCollection AddEasyStockNotificationsRepositories(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<ITemplateRepository, TemplateNotificacaoRepository>();
        services.AddScoped<IRotinaRepository, RotinaNotificacaoRepository>();
        services.AddScoped<IEventoNotificacaoRepository, EventoNotificacaoRepository>();
        services.AddScoped<IOutboxNotificacaoRepository, OutboxNotificacaoRepository>();
        services.AddScoped<IConsentimentoRepository, ConsentimentoRepository>();
        services.AddScoped<IPreferenciaNotificacaoRepository, PreferenciaNotificacaoRepository>();
        services.AddScoped<IAudienciaUsuarios, AudienciaUsuarios>();
        services.AddScoped<ISuperAdminsDaPlataforma, SuperAdminsDaPlataformaQuery>();
        services.AddScoped<IConfiguracaoCanalRepository, ConfiguracaoCanalRepository>();
        services.AddScoped<IBloqueioNotificacaoRepository, BloqueioNotificacaoRepository>();
        services.AddScoped<ILogEnvioNotificacaoRepository, LogEnvioNotificacaoRepository>();
        services.AddScoped<IVariavelTemplateCatalogoRepository, VariavelTemplateCatalogoRepository>();
        // Onda 2.2 — subscriptions de Web Push (PWA).
        services.AddScoped<IWebPushSubscriptionRepository, WebPushSubscriptionRepository>();

        // Backlog agregado do motor (N1): health check de backlog e ping do Worker.
        services.AddScoped<IBacklogNotificacoes, BacklogNotificacoesQuery>();

        // Quarentena (N1): o dispatcher expira o outbox pelo prazo do tipo. TryAdd: AddEasyStockApplication também o
        // registra e as sobrescritas (Notifications:Quarentena) são bindadas em AddNotificationsCore.
        services.AddOptions();
        services.TryAddSingleton<PoliticaValidadeNotificacao>();

        // Dispatcher orchestrator — implementa também o port INotificationDispatcher (1 shard).
        // Singleton porque é stateless e cria scopes internamente via IServiceProvider.
        services.AddSingleton<NotificacoesDispatcherOrchestrator>();
        services.AddSingleton<INotificacoesDispatcherOrchestrator>(sp => sp.GetRequiredService<NotificacoesDispatcherOrchestrator>());
        services.AddSingleton<INotificationDispatcher>(sp => sp.GetRequiredService<NotificacoesDispatcherOrchestrator>());

        // Coletores de eventos de estado — vivem em Infra.Postgre porque dependem de
        // EasyStockDbContext. Worker e API ambos consomem via INotificacoesColetorOrchestrator.
        // TryAddEnumerable: AddEasyStockPostgreInfrastructure já chama este registro; o Worker o chamava de novo e o
        // coletor rodava duas vezes por rodada (N1).
        // N12: rotinas agendadas por horário diário local (resumo diário e o que vier). Relógio injetável para o teste.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IColetorEventoNotificacao, ColetorRotinasAgendadas>());

        // N12 (Q3, decisão do Felipe): produtos vencendo fica fora. O coletor tem três defeitos conhecidos (data UTC,
        // CorrelationId de 65 caracteres numa coluna de 64 e payload que não casa com o template); registrado, ele falharia a
        // cada 5 min e poluiria o log. Só liga com Notifications:Coletores:ProdutosVencendo:Habilitado=true.
        if (configuration?.GetValue<bool>(ProdutosVencendoHabilitadoChave) == true)
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IColetorEventoNotificacao, ColetorProdutosVencendo>());
        // N10: pico de 5xx. Só lê o COUNT de system_error_logs (fora da RLS) e avisa pelo publicador de incidente.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IColetorEventoNotificacao, ColetorPicoDeErros5xx>());

        return services;
    }

    /// <summary>
    /// Registra <see cref="PostgresOutboxSignaler"/> como singleton + IHostedService —
    /// MAS apenas quando <c>Notifications:Hosting:Mode == Hosted</c> e
    /// <c>Notifications:Hosting:Signaler == Postgres</c>. Caso contrário é no-op.
    /// <para>
    /// Antes desse fix, o signaler era registrado incondicionalmente — em modos
    /// "Disabled" (API default) ou "Polling", abria conexão LISTEN/NOTIFY zumbi
    /// que ninguém consumia. Agora chamada é seguro em qualquer host.
    /// </para>
    /// </summary>
    public static IServiceCollection AddPostgresOutboxSignaler(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // Se não passar configuration, registra incondicional (compat). Recomendado passar.
        if (configuration is not null)
        {
            var opts = configuration
                .GetSection(NotificationsHostingOptions.Section)
                .Get<NotificationsHostingOptions>() ?? new NotificationsHostingOptions();

            if (opts.Mode != NotificationsHostingMode.Hosted ||
                opts.Signaler != OutboxSignalerKind.Postgres)
            {
                return services;
            }
        }

        services.AddSingleton<PostgresOutboxSignaler>();
        services.AddSingleton<IOutboxSignaler>(sp => sp.GetRequiredService<PostgresOutboxSignaler>());
        services.AddHostedService(sp => sp.GetRequiredService<PostgresOutboxSignaler>());

        // Anonimização de logs antigos — pertence ao pipeline de notificações (não Helpdesk),
        // só faz sentido em Mode=Hosted (rodando in-process aqui). Compartilha NotificationsHostingOptions.
        services.AddHostedService<AnonimizarLogsAntigosService>();

        return services;
    }
}
