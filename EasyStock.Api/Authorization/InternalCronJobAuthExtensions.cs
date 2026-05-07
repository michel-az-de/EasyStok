using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.Authorization;

public static class InternalCronJobAuthExtensions
{
    /// <summary>
    /// Adiciona o scheme + policy "InternalCronJob" para endpoints internos de gatilho HTTP
    /// de jobs (cron externo). Idempotente — pode ser chamado mesmo se já registrado.
    /// </summary>
    public static AuthenticationBuilder AddInternalCronJobScheme(this AuthenticationBuilder builder)
    {
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
