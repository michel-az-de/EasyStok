using Microsoft.Extensions.FileProviders;

namespace EasyStock.Api.Mobile;

/// <summary>
/// Pipeline do módulo Casa da Baba Mobile no EasyStock.
///
/// Não chama <c>AddControllers()</c>, <c>AddCors()</c>, <c>UseCors()</c> nem
/// <c>MapControllers()</c> — todos já existem no <c>Program.cs</c> via
/// <c>ApiServiceCollectionExtensions</c>. Só adiciona o static-files
/// customizado do <c>/pwa/</c> com headers específicos para o service worker.
/// </summary>
public static class MobileModule
{
    /// <summary>
    /// Serve <c>wwwroot/pwa/</c> em <c>/pwa/</c> com headers de service worker
    /// e redirects convenientes (<c>/pwa</c> e <c>/app</c> → <c>/pwa/index.html</c>).
    /// Chame DEPOIS de <c>app.UseStaticFiles()</c> existente e ANTES de
    /// <c>app.MapControllers()</c>.
    /// </summary>
    public static WebApplication UseMobilePwa(this WebApplication app)
    {
        var webRoot = app.Environment.WebRootPath;
        if (string.IsNullOrEmpty(webRoot))
        {
            // WebRootPath pode ser null em alguns layouts; fallback para ContentRoot/wwwroot.
            webRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        }

        var pwaRoot = Path.Combine(webRoot, "pwa");
        if (!Directory.Exists(pwaRoot))
        {
            // PWA não foi publicado ainda — sem problema, módulo fica inativo.
            return app;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(pwaRoot),
            RequestPath = "/pwa",
            OnPrepareResponse = ctx =>
            {
                if (ctx.File.Name.Equals("sw.js", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.Headers["Service-Worker-Allowed"] = "/pwa/";
                    ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
                }
            }
        });

        app.MapGet("/pwa", () => Results.Redirect("/pwa/index.html")).ExcludeFromDescription();
        app.MapGet("/app", () => Results.Redirect("/pwa/index.html")).ExcludeFromDescription();

        return app;
    }
}
