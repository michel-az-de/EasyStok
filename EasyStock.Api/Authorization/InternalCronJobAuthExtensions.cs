using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.Authorization;

public static class InternalCronJobAuthExtensions
{
    /// <summary>
    /// Adiciona o scheme + policy "InternalCronJob" para endpoints internos de gatilho HTTP
    /// de jobs (cron externo). Bind de <see cref="InternalCronJobOptions"/> via
    /// <c>Notifications:CronJob</c> permite hot-reload de Habilitado/Token sem restart.
    /// </summary>
    public static AuthenticationBuilder AddInternalCronJobScheme(
        this AuthenticationBuilder builder, IConfiguration configuration)
    {
        builder.Services.Configure<InternalCronJobOptions>(
            configuration.GetSection(InternalCronJobOptions.Section));

        return builder.AddScheme<AuthenticationSchemeOptions, InternalCronJobAuthHandler>(
            InternalCronJobAuthHandler.SchemeName, _ => { });
    }

    /// <summary>
    /// Configura a policy "InternalCronJob" para exigir autenticação via scheme do mesmo nome.
    /// </summary>
    public static AuthorizationOptions AddInternalCronJobPolicy(this AuthorizationOptions options)
    {
        options.AddPolicy(InternalCronJobAuthHandler.PolicyName, policy =>
        {
            policy.AuthenticationSchemes = new[] { InternalCronJobAuthHandler.SchemeName };
            policy.RequireAuthenticatedUser();
        });
        return options;
    }
}
