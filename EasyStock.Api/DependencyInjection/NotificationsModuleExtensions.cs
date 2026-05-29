using EasyStock.Infra.Notifications.DependencyInjection;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using Serilog;

namespace EasyStock.Api.DependencyInjection;

/// <summary>
/// Extensions para registrar o módulo de notificações (infra de canais + hosting + signaler)
/// e logar warning quando o pipeline está rodando in-process na API.
///
/// Hosting fica como "Disabled" por default na API — ative trocando
/// "Notifications:Hosting:Mode" para "Hosted" se quiser rodar o pipeline in-process
/// (modo sem Worker). AddPostgresOutboxSignaler é no-op se Mode=Disabled ou
/// Signaler!=Postgres (ver impl).
/// </summary>
public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddEasyStockNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddNotificationsInfra(configuration);
        services
            .AddNotificationsHosting(configuration)
            .AddPostgresOutboxSignaler(configuration);
        services.AddScoped<PostgresAdvisoryLock>();

        // Aviso explicito quando o pipeline de notificacoes vai rodar in-process: nao ha bulkhead
        // real entre HTTP da API e os 3 loops (compartilham ThreadPool, GC e memoria). Modo
        // suportado para Render free tier ou dev/teste; em producao prefira Worker como deploy
        // separado (Notifications:Hosting:Mode=Disabled aqui + Mode=Hosted no Worker).
        var notifMode = configuration["Notifications:Hosting:Mode"];
        if (string.Equals(notifMode, "Hosted", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "Notifications:Hosting:Mode=Hosted na API — pipeline rodando in-process. " +
                "Sem isolamento de processo entre HTTP e loops; monitore /health/dispatcher.");
        }

        return services;
    }
}
