using EasyStock.Application.Services.Notifications;
using EasyStock.Infra.Notifications.DependencyInjection;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using Serilog;

namespace EasyStock.Api.DependencyInjection;

/// <summary>
/// Extensions para registrar o módulo de notificações (infra de canais, hosting e signaler) na API.
///
/// <para>
/// <b>Um host só (N1):</b> a API NUNCA hospeda os loops do motor (Dispatcher, Avaliador, Coletor, sinalizador e
/// anonimizador): quem roda é o Worker. O modo é forçado a <c>Disabled</c> por código e o valor
/// <c>Notifications:Hosting:Mode=Hosted</c> da configuração é ignorado, com aviso no log. Antes o padrão era
/// <c>Hosted</c>: com API e Worker de pé, os dois avaliavam o mesmo evento (o Avaliador e o Coletor não têm lock) e o
/// índice único do outbox barrava a segunda linha com 23505. Os gatilhos <c>api/internal/notif-jobs/*</c> seguem
/// disponíveis para rodar uma rodada sob demanda.
/// </para>
/// </summary>
public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddEasyStockNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // A configuracao vista pelo modulo e a da API com o modo forcado a Disabled: AddNotificationsHosting nao
        // registra os loops, AddPostgresOutboxSignaler nao registra o sinalizador nem o anonimizador, e o health
        // /health/dispatcher le Disabled das opcoes ("disabled neste host").
        var configuracaoDaApi = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{NotificationsHostingOptions.Section}:Mode"] = nameof(NotificationsHostingMode.Disabled)
            })
            .Build();

        services.AddNotificationsInfra(configuracaoDaApi);
        services
            .AddNotificationsHosting(configuracaoDaApi)
            .AddPostgresOutboxSignaler(configuracaoDaApi);
        services.AddScoped<PostgresAdvisoryLock>();
        // N11: limites e interruptor dos quatro prazos que chegam por e-mail e WhatsApp.
        services.Configure<EasyStock.Application.Services.Notifications.PrazosOptions>(
            configuration.GetSection(EasyStock.Application.Services.Notifications.PrazosOptions.Section));

        var notifMode = configuration[$"{NotificationsHostingOptions.Section}:Mode"];
        if (string.Equals(notifMode, nameof(NotificationsHostingMode.Hosted), StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "Notifications:Hosting:Mode=Hosted na API foi ignorado: os loops do motor rodam so no Worker. " +
                "Use api/internal/notif-jobs/* para uma rodada sob demanda.");
        }

        return services;
    }
}
