using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.Hosting;

public static class NotificationsHostingServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="NotificationsHostingOptions"/> bindado da seção
    /// <c>Notifications:Hosting</c>. Idempotente — pode ser chamado em qualquer host (API, Worker, cron-only).
    /// Os orchestrators e repositórios devem ser registrados pelos consumidores via
    /// <c>AddEasyStockApplication()</c> e <c>AddEasyStockNotificationsRepositories()</c>.
    /// </summary>
    public static IServiceCollection AddNotificationsCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<NotificationsHostingOptions>(
            configuration.GetSection(NotificationsHostingOptions.Section));

        // Heartbeat e usado pelos wrappers Hosted E pelo health check (NotificationsHostingHealthCheck).
        // Singleton sempre — custo zero quando Mode=Disabled (so guarda dictionary vazio) e simplifica
        // o registro pra hosts que so precisam do health check (API com pipeline em Worker separado).
        services.TryAddSingleton<INotificationsLoopHeartbeat, NotificationsLoopHeartbeat>();

        // Retro-compat: se a seção legada "Worker" existir, copia chaves equivalentes.
        var legacyWorker = configuration.GetSection("Worker");
        if (legacyWorker.Exists())
        {
            services.PostConfigure<NotificationsHostingOptions>(opts =>
            {
                if (int.TryParse(legacyWorker["ShardCount"], out var sc) && sc > 0)
                    opts.ShardCount = sc;
                if (int.TryParse(legacyWorker["DispatcherBatchSize"], out var bs) && bs > 0)
                    opts.DispatcherBatchSize = bs;
                if (int.TryParse(legacyWorker["DispatcherPollingIntervalMs"], out var pi) && pi > 0)
                    opts.DispatcherPollingIntervalMs = pi;
                if (int.TryParse(legacyWorker["AvaliadoresIntervalSeconds"], out var ai) && ai > 0)
                    opts.AvaliadorIntervalSeconds = ai;
                if (int.TryParse(legacyWorker["ColetorIntervalSeconds"], out var ci) && ci > 0)
                    opts.ColetorIntervalSeconds = ci;
            });
        }

        return services;
    }

    /// <summary>
    /// Registra os 3 wrappers BackgroundService (Dispatcher, Avaliador, Coletor) quando
    /// <see cref="NotificationsHostingOptions.Mode"/> = <see cref="NotificationsHostingMode.Hosted"/>.
    /// Para signaler do tipo <see cref="OutboxSignalerKind.Polling"/>, registra
    /// <see cref="PollingOutboxSignaler"/>. Para <see cref="OutboxSignalerKind.Postgres"/>,
    /// o consumidor deve ter registrado <c>IOutboxSignaler</c> separadamente via
    /// <c>AddPostgresOutboxSignaler()</c> (extension em Infra.Postgre).
    /// </summary>
    public static IServiceCollection AddNotificationsHosting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Garante que options foi bindado (idempotente).
        services.AddNotificationsCore(configuration);

        var opts = configuration
            .GetSection(NotificationsHostingOptions.Section)
            .Get<NotificationsHostingOptions>() ?? new NotificationsHostingOptions();

        if (opts.Mode != NotificationsHostingMode.Hosted)
            return services;

        // Polling signaler — registrado aqui (não depende de Infra.Postgre).
        // Postgres signaler — consumidor registra via AddPostgresOutboxSignaler() em Infra.Postgre.
        if (opts.Signaler == OutboxSignalerKind.Polling)
        {
            const int MinIntervaloMs = 1_000;
            var intervaloMs = opts.DispatcherPollingIntervalMs;
            services.AddSingleton<IOutboxSignaler>(sp =>
            {
                var efetivo = intervaloMs;
                if (efetivo < MinIntervaloMs)
                {
                    var log = sp.GetRequiredService<ILogger<PollingOutboxSignaler>>();
                    log.LogWarning(
                        "Notifications:Hosting:DispatcherPollingIntervalMs={Configurado}ms abaixo do mínimo {Min}ms — usando {Efetivo}ms",
                        intervaloMs, MinIntervaloMs, MinIntervaloMs);
                    efetivo = MinIntervaloMs;
                }
                return new PollingOutboxSignaler(TimeSpan.FromMilliseconds(efetivo));
            });
        }

        // Wrappers de loop
        services.AddHostedService<DispatcherLoopHostedService>();
        services.AddHostedService<AvaliadorLoopHostedService>();
        services.AddHostedService<ColetorLoopHostedService>();

        return services;
    }
}
