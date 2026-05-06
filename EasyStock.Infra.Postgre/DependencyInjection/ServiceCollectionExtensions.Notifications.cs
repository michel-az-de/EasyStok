using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Postgre.Repositories.Notifications;
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

        return services;
    }
}
