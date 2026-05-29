using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.ResponseCompression;

namespace EasyStock.Api.DependencyInjection;

/// <summary>
/// Extensions para registrar MVC core + JSON + Response Compression do EasyStock.Api.
///
/// Mantém a mesma ordem relativa de registro do Program.cs original:
/// AddControllers → JsonOptions → FluentValidationAutoValidation →
/// HttpContextAccessor → EndpointsApiExplorer → ResponseCompression →
/// Configure&lt;BrotliCompressionProviderOptions&gt; → Configure&lt;GzipCompressionProviderOptions&gt;.
/// </summary>
public static class CoreMvcExtensions
{
    public static IServiceCollection AddEasyStockCoreMvc(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(opts =>
            {
                opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                opts.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
        services.AddFluentValidationAutoValidation();
        services.AddHttpContextAccessor();
        services.AddEndpointsApiExplorer();

        // Response compression — Brotli/Gzip para JSON e estaticos (PWA). Reduz bandwidth
        // significativamente em listagens grandes (catalogos, mobile sync). Render cobra
        // bandwidth acima do free tier; CPU overhead e' marginal.
        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
            {
                "application/json",
                "application/javascript",
                "image/svg+xml"
            });
        });
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

        return services;
    }
}
