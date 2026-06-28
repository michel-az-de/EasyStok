using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Admin.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// Render REAL (WebApplicationFactory) das telas do piloto de pedagogia do Storefront
/// (#713) + Central de Ajuda. Cobertura de RUNTIME que o build-check (só compila) não
/// dá: um bug de render nos TagHelpers novos (es-page-header, es-help, checklist de
/// prontidão) cairia aqui como 500/302 em vez de 200. Auth fake SuperAdmin + API stubada
/// com envelope de storefront/cardápio (espelha XssRenderTests).
/// </summary>
public class StorefrontPedagogiaRenderTests : IClassFixture<StorefrontPedagogiaRenderTests.StorefrontFactory>
{
    public static readonly Guid SfId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly StorefrontFactory _factory;
    public StorefrontPedagogiaRenderTests(StorefrontFactory factory) => _factory = factory;

    private async Task<(HttpStatusCode Code, string Html)> Get(string rota)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync(rota);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CentralDeAjuda_renderiza_glossario()
    {
        var (code, html) = await Get("/Ajuda");
        code.Should().Be(HttpStatusCode.OK, "render falhou. body: {0}", html.Length > 1500 ? html[..1500] : html);
        html.Should().Contain("Central de Ajuda");
        html.Should().Contain("es-page-header-title"); // es-page-header renderizou
        html.Should().Contain("Fatura");                // verbete do glossário (âncora /Ajuda#fatura)
    }

    [Fact]
    public async Task StorefrontDetail_renderiza_pageheader_prontidao_help_e_ativar()
    {
        var (code, html) = await Get($"/Storefronts/Detail/{SfId}");
        code.Should().Be(HttpStatusCode.OK, "render falhou. body: {0}", html.Length > 1500 ? html[..1500] : html);
        html.Should().Contain("es-page-header-title");   // es-page-header (P2)
        html.Should().Contain("Prontidão para ativar");  // checklist (P3)
        html.Should().Contain("es-help-trigger");        // es-help do help-term (gatilho)
        html.Should().Contain("Ativar storefront");      // botão ativar (P4; storefront inativo no stub)
    }

    public sealed class StorefrontFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ApiBaseUrl", "https://api.test.local");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<AdminSessionService, FakeSuperAdminSession>();
                services.AddHttpClient<AdminApiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => new StorefrontStubHandler());
            });
        }
    }

    private sealed class FakeSuperAdminSession(IHttpContextAccessor accessor) : AdminSessionService(accessor)
    {
        public override string? GetToken()
        {
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"nivel\":\"SuperAdmin\"}"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return "h." + payload + ".s";
        }

        public override string? GetRefreshToken() => null;
    }

    private sealed class StorefrontStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            object envelope = path.EndsWith("/cardapio", StringComparison.OrdinalIgnoreCase)
                ? new { data = new { storefrontSlug = "loja-teste", storefrontTitulo = "Loja Teste", itens = Array.Empty<object>() } }
                : new
                {
                    data = new
                    {
                        id = SfId,
                        empresaId = Guid.NewGuid(),
                        empresaNome = "Acme Ltda",
                        slug = "loja-teste",
                        tituloPublico = "Loja Teste",
                        subtituloPublico = (string?)null,
                        dominioCustom = (string?)null,
                        logoUrl = (string?)null,
                        corPrimaria = (string?)null,
                        whatsappPedidos = (string?)null,
                        pedidoMinimoEntrega = 0m,
                        freteGratisAcima = (decimal?)null,
                        mensagemForaArea = (string?)null,
                        modeloFiscal = "manual",
                        nfeAutomaticaHabilitada = false,
                        lojaPadraoId = (Guid?)null,
                        ativo = false,
                        cardapioCount = 0,
                        criadoEm = "2026-01-15T10:00:00Z",
                        alteradoEm = "2026-01-15T10:00:00Z"
                    }
                };

            var json = JsonSerializer.Serialize(envelope);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
