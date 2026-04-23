using EasyStock.Mobile.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System.IO;

namespace EasyStock.Mobile;

/// <summary>
/// Extensoes pro Program.cs / Startup.cs do EasyStock. Centraliza o registro
/// de DbSets, controllers, CORS e static files do PWA.
///
/// Uso no Program.cs:
///
///   var builder = WebApplication.CreateBuilder(args);
///   // ...
///   builder.Services.AddMobileModule();
///
///   var app = builder.Build();
///   app.UseMobileModule();
///   // ...
///   app.Run();
/// </summary>
public static class MobileModule
{
    /// <summary>
    /// Registra services necessarios pro modulo mobile.
    /// Chame ANTES de builder.Build().
    /// </summary>
    public static IServiceCollection AddMobileModule(this IServiceCollection services)
    {
        services.AddControllers();

        // CORS aberto pra desenvolvimento. Em producao, restrinja Origins.
        services.AddCors(options =>
        {
            options.AddPolicy("MobilePwa", policy =>
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        });

        return services;
    }

    /// <summary>
    /// Configura pipeline: CORS, controllers, static files do PWA.
    /// Chame DEPOIS de app.Build() e ANTES de app.Run().
    /// </summary>
    public static WebApplication UseMobileModule(this WebApplication app, string pwaPath = "pwa")
    {
        app.UseCors("MobilePwa");
        app.MapControllers();

        // Resolve o caminho do PWA.
        // Prioridade: caminho absoluto > WebRootPath/pwa > ContentRoot/wwwroot/pwa
        string absolute;
        if (Path.IsPathRooted(pwaPath))
        {
            absolute = pwaPath;
        }
        else if (!string.IsNullOrEmpty(app.Environment.WebRootPath))
        {
            absolute = Path.Combine(app.Environment.WebRootPath, pwaPath);
        }
        else
        {
            absolute = Path.Combine(app.Environment.ContentRootPath, "wwwroot", pwaPath);
        }

        if (Directory.Exists(absolute))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(absolute),
                RequestPath = "/pwa",
                OnPrepareResponse = ctx =>
                {
                    // Service worker precisa ter escopo correto
                    if (ctx.File.Name == "sw.js")
                    {
                        ctx.Context.Response.Headers.Append("Service-Worker-Allowed", "/pwa/");
                        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache");
                    }
                }
            });

            // Redireciona /pwa pra /pwa/index.html
            app.MapGet("/pwa", () => Results.Redirect("/pwa/index.html"));
            // Shortcut: /app -> /pwa/index.html
            app.MapGet("/app", () => Results.Redirect("/pwa/index.html"));
        }

        return app;
    }

    /// <summary>
    /// Registra os DbSets do mobile no DbContext existente do EasyStock.
    /// Chame de dentro do OnModelCreating do seu DbContext:
    ///
    ///   protected override void OnModelCreating(ModelBuilder mb) {
    ///       base.OnModelCreating(mb);
    ///       mb.RegisterMobileModels();
    ///   }
    /// </summary>
    public static ModelBuilder RegisterMobileModels(this ModelBuilder mb)
    {
        mb.Entity<Product>();
        mb.Entity<Client>();
        mb.Entity<Order>()
            .HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<OrderItem>();
        mb.Entity<Batch>()
            .HasMany(b => b.Items)
            .WithOne(i => i.Batch)
            .HasForeignKey(i => i.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<BatchItem>();
        mb.Entity<CashEntry>();

        return mb;
    }
}
