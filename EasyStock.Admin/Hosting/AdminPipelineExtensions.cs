using EasyStock.Admin.Middleware;
using Microsoft.AspNetCore.HttpOverrides;

namespace EasyStock.Admin.Hosting;

/// <summary>
/// Pipeline + endpoints estáticos do <c>EasyStock.Admin</c>. Transcrição verbatim do que
/// vivia inline no <c>Program.cs</c>:
/// <list type="number">
///   <item>UseForwardedHeaders (XForwardedFor/Proto/Host — TLS termination edge)</item>
///   <item>UseExceptionHandler("/Error") + UseHsts (não-Development)</item>
///   <item>UseResponseCompression + UseHttpsRedirection + UseStaticFiles</item>
///   <item>UseRouting + UseSession + UseAuthentication + UseAuthorization</item>
///   <item>MapRazorPages</item>
///   <item>Redirects estáticos: /Clientes → /Tenants, /Status → /Diagnostico (301)</item>
/// </list>
///
/// Proxies <c>/api-proxy/*</c> são registrados separadamente em
/// <see cref="ApiProxyEndpoints.MapAdminApiProxies"/>.
/// </summary>
public static class AdminPipelineExtensions
{
    public static void UseEasyStockAdminPipeline(this WebApplication app)
    {
        // ForwardedHeaders: Fly/Render/etc fazem TLS no edge e mandam HTTP com
        // X-Forwarded-Proto=https. Sem isso o UseHttpsRedirection estoura 400.
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost,
            KnownNetworks = { },
            KnownProxies = { }
        });

        // ── Middleware ────────────────────────────────────────────────────────────
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        // Status codes sem corpo (404/403/...) re-executam a página /Error amigável.
        // Fora do if(!Development) de propósito: queremos a página amigável também em dev
        // (e testável localmente), em vez do 404 cru do Kestrel.
        app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

        app.UseResponseCompression();
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseSession();
        app.UseAuthentication();
        app.UseAuthorization();

        // Restaura a sessao via cookie _rt_admin quando a sessao in-memory foi zerada
        // por deploy/restart — roda antes do AdminPageBase (que redireciona pro login
        // se a sessao estiver vazia). Safe-by-construction: ver AdminSessionRestoreMiddleware.
        app.UseMiddleware<AdminSessionRestoreMiddleware>();

        app.MapRazorPages();

        // Aliases /Clientes → /Tenants (sidebar label foi renomeada na slice de Gestão de Cliente,
        // mas as rotas internas seguem `/Tenants`. Redirect mantém URLs digitadas funcionando).
        app.MapGet("/Clientes", () => Results.Redirect("/Tenants", permanent: false));
        app.MapGet("/Clientes/Detail/{id:guid}", (Guid id) => Results.Redirect($"/Tenants/Detail/{id}", permanent: false));

        // /Status absorvido em /Diagnostico (slice "Diagnóstico de Erros + Seed Visível").
        // Redirect 301 mantém bookmarks/links externos funcionando. Remover daqui a 1-2 releases.
        app.MapGet("/Status", () => Results.Redirect("/Diagnostico", permanent: true));
    }
}
