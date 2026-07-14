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
        //
        // CRITICO (pentest #913): com KnownNetworks/KnownProxies VAZIOS o middleware só
        // confia no loopback, então atrás do Caddy (bridge Docker 172.x) ele DESCARTA o
        // X-Forwarded-Proto=https -> scheme=http -> redirect de login http:// e HSTS não
        // emite. Espelha o padrão da API: confiar nas faixas privadas (o Admin só é
        // alcançado pela rede interna do Docker) + ForwardLimit=1 (consome só a entrada
        // mais à direita, injetada pelo proxy confiável; XFF forjado à esquerda é ignorado).
        var forwardedHeaders = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost,
            ForwardLimit = 1
        };
        forwardedHeaders.KnownNetworks.Clear();
        forwardedHeaders.KnownProxies.Clear();
        foreach (var rede in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8" })
            forwardedHeaders.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(rede));
        app.UseForwardedHeaders(forwardedHeaders);

        // Headers de seguranca (issue 818). CSP em Report-Only nesta primeira fatia:
        // o Admin usa Alpine com x-data/x-on inline (exige 'unsafe-eval'/'unsafe-inline'),
        // entao o enforce so entra depois de observar os reports sem quebrar tela.
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Content-Security-Policy-Report-Only"] =
                "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; " +
                "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";
            await next();
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
