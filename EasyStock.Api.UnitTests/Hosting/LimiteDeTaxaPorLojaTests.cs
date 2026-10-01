using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EasyStock.Api.Configuration;
using EasyStock.Api.Hosting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.UnitTests.Hosting;

/// <summary>
/// #1246: o botão Testar das integrações tem 6 chamadas por minuto por loja e provider. A partição lê o
/// claim <c>empresaId</c>, que só existe depois da autenticação: com o limitador antes dela, todas as
/// lojas atrás do mesmo IP dividem a mesma cota. Sobe um host real (Kestrel, porta livre) com a política
/// de produção (<see cref="ApiServiceCollectionExtensions.AddEasyStockRateLimit"/>) e a ordem de produção
/// (<see cref="PipelineExtensions.UseAutenticacaoELimiteDeTaxa"/>).
/// </summary>
public class LimiteDeTaxaPorLojaTests
{
    private const string Esquema = "TesteEmpresa";

    [Fact]
    public async Task CadaLojaTemSuaPropriaCotaDoTestar()
    {
        await using var app = await SubirAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        for (var i = 0; i < IntegracaoTesteRateLimit.PorMinuto; i++)
            (await TestarAsync(http, "loja-a")).Should().Be(HttpStatusCode.OK);
        (await TestarAsync(http, "loja-a")).Should().Be(HttpStatusCode.TooManyRequests);

        (await TestarAsync(http, "loja-b")).Should().Be(HttpStatusCode.OK, "a cota da loja A não pode bloquear a loja B");
    }

    private static Task<HttpStatusCode> TestarAsync(HttpClient http, string empresa)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/integracoes/mercadopago/testar");
        pedido.Headers.Add("X-Empresa", empresa);
        return http.SendAsync(pedido).ContinueWith(t => t.Result.StatusCode);
    }

    private static async Task<WebApplication> SubirAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddEasyStockRateLimit();
        builder.Services.AddAuthentication(Esquema).AddScheme<AuthenticationSchemeOptions, EmpresaPeloCabecalho>(Esquema, _ => { });
        builder.Services.AddRouting();

        var app = builder.Build();
        app.UseRouting();
        app.UseAutenticacaoELimiteDeTaxa();
        app.MapPost("/api/integracoes/{provider}/testar", () => Results.Ok())
            .RequireRateLimiting(IntegracaoTesteRateLimit.Politica);
        await app.StartAsync();
        return app;
    }

    /// <summary>Autenticação de teste: o claim <c>empresaId</c> vem do cabeçalho <c>X-Empresa</c>.</summary>
    private sealed class EmpresaPeloCabecalho(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var empresa = Request.Headers["X-Empresa"].ToString();
            if (string.IsNullOrEmpty(empresa)) return Task.FromResult(AuthenticateResult.NoResult());
            var identidade = new ClaimsIdentity([new Claim("empresaId", empresa)], Esquema);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidade), Esquema)));
        }
    }
}
