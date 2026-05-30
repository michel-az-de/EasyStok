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
/// Teste comportamental do F0: renderiza a página real Tenants/Detail (via WebApplicationFactory)
/// com a API stubada devolvendo nome de usuário/loja contendo payload de XSS. Assere sobre o
/// HTML EMITIDO PELO SERVIDOR — que é o único sink (o Alpine só faz x-text/x-model do valor,
/// provado na review). VERMELHO no código pré-fix (atributo quebrava), VERDE com data-*.
/// </summary>
public class XssRenderTests : IClassFixture<XssRenderTests.AdminFactory>
{
    // Payloads em campos controlados por tenant.
    public const string AttrBreakout = "a\" onmouseover=\"alert(document.cookie)\" x=\"";
    public const string TagBreakout = "</script><img src=x onerror=alert(1)>";
    public const string LegitName = "João's Café & R&D";

    private readonly AdminFactory _factory;
    public XssRenderTests(AdminFactory factory) => _factory = factory;

    [Fact]
    public async Task TenantDetail_nome_de_tenant_nao_quebra_atributo_nem_injeta_handler()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync($"/Tenants/Detail/{AdminFactory.TenantId}");
        var html = await resp.Content.ReadAsStringAsync();

        // Auth (token SuperAdmin falso) passou e a página renderizou — não 302/login nem 500.
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "render falhou. body: {0}",
            html.Length > 1500 ? html[..1500] : html);

        // Fix presente: o dado vai por data-* (encoder de atributo do Razor).
        html.Should().Contain("data-unome=");

        // O handler/​tag maliciosos NÃO aparecem como atributo/elemento real.
        html.Should().NotContain("onmouseover=\"alert(document.cookie)\"");
        html.Should().NotContain("<img src=x onerror=alert(1)>");

        // O conteúdo perigoso está presente, porém INERTE (aspas viraram entidade &quot;).
        html.Should().Contain("onmouseover=&quot;alert(document.cookie)&quot;");

        // Não usa escape JS manual: sem sequências \u quebrando nomes.
        html.Should().NotContain("\\u0027");
        html.Should().NotContain("\\u0022");
    }

    public sealed class AdminFactory : WebApplicationFactory<Program>
    {
        public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Startup do Admin exige ApiBaseUrl (fail-fast). Fornece um valor de teste.
            builder.UseSetting("ApiBaseUrl", "https://api.test.local");
            // Development → página de erro detalhada no body (facilita diagnosticar render).
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                // Auth sem semear sessão real: token SuperAdmin sintético
                // (IsSuperAdmin só decodifica o payload, não valida assinatura — defesa em profundidade, F6).
                services.AddScoped<AdminSessionService, FakeSuperAdminSession>();

                // API stubada: toda chamada devolve o envelope do tenant com payloads de XSS.
                services.AddHttpClient<AdminApiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => new TenantStubHandler());
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

    private sealed class TenantStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var tenant = new
            {
                empresa = new { id = AdminFactory.TenantId, nome = "Acme Ltda", status = "Ativa", documento = "12345678000190", criadoEm = "2026-01-01T00:00:00Z" },
                assinatura = new { plano = new { nome = "Pro" }, status = "Ativa" },
                auditLogRecentes = Array.Empty<object>(),
                usuarios = new object[]
                {
                    new { id = Guid.NewGuid(), nome = AttrBreakout, email = "evil@x.com", nivelAcesso = "Admin", ativo = true, ultimoAcessoEm = (string?)null },
                    new { id = Guid.NewGuid(), nome = LegitName, email = "joao@x.com", nivelAcesso = "Operador", ativo = true, ultimoAcessoEm = (string?)null },
                },
                lojas = new object[]
                {
                    new { id = Guid.NewGuid(), nome = TagBreakout, descricao = "", documento = "", endereco = "", telefone = "", ativa = true },
                },
            };

            var json = JsonSerializer.Serialize(new { data = tenant });
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
