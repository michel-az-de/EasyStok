using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Api.Authentication;

namespace EasyStock.Api.Middleware;

/// <summary>
/// Sliding window middleware para sessões storefront (ADR-0012).
///
/// <para>
/// A cada request autenticado com <c>__Host-cdb_session</c>, atualiza
/// <c>ClienteSession.UltimoUsoEm</c> para "agora" — mantendo a janela
/// de 30 dias deslizante enquanto o cliente usa o app.
/// </para>
///
/// <para>
/// Executado apenas quando o request já foi autenticado com sucesso pelo
/// <see cref="ClienteSessionAuthenticationHandler"/>.
/// </para>
/// </summary>
public class ClienteSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        // Atualiza sliding window apenas se a sessão foi autenticada com sucesso
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.Identity.AuthenticationType == ClienteSessionAuthenticationHandler.SchemeName)
        {
            var sidClaim = context.User.FindFirst("sid")?.Value;
            if (Guid.TryParse(sidClaim, out var sessionId))
            {
                var sessionRepo = context.RequestServices.GetRequiredService<IClienteSessionRepository>();
                var unitOfWork = context.RequestServices.GetRequiredService<IUnitOfWork>();
                var timeProvider = context.RequestServices.GetRequiredService<TimeProvider>();

                var session = await sessionRepo.GetByIdAsync(sessionId);
                if (session is not null && session.EstaValida(timeProvider))
                {
                    session.RegistrarUso(timeProvider);
                    await sessionRepo.UpdateAsync(session);
                    await unitOfWork.CommitAsync();
                }
            }
        }
    }
}
