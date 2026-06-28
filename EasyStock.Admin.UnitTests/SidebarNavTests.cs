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
/// Poka-yoke do menu lateral (QA BUG-001 / issue 729): o link "Config. Fiscal" apontava para
/// <c>/Configuracoes/ConfiguracaoFiscal</c>, que exige EmpresaId e nunca o recebia do menu →
/// sempre erro. Foi removido. Este teste renderiza uma página real (com a sidebar) e assere
/// que o link pendurado NÃO aparece, mantendo o link "Configurações" que funciona. VERMELHO se
/// alguém reintroduzir a entrada sem contexto de empresa.
/// </summary>
public class SidebarNavTests : IClassFixture<SidebarNavTests.AdminFactory>
{
    private readonly AdminFactory _factory;
    public SidebarNavTests(AdminFactory factory) => _factory = factory;

    [Fact]
    public async Task Sidebar_nao_linka_ConfiguracaoFiscal_sem_empresaId()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync($"/Tenants/Detail/{AdminFactory.TenantId}");
        var html = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "render falhou. body: {0}",
            html.Length > 1500 ? html[..1500] : html);

        // O link pendurado (sempre erra sem EmpresaId) nao pode estar na sidebar.
        html.Should().NotContain("/Configuracoes/ConfiguracaoFiscal");
        // A entrada de Configuracoes (que funciona) permanece.
        html.Should().Contain("href=\"/Configuracoes\"");
    }

    public sealed class AdminFactory : WebApplicationFactory<Program>
    {
        public static readonly Guid TenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ApiBaseUrl", "https://api.test.local");
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<AdminSessionService, FakeSuperAdminSession>();
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
                usuarios = Array.Empty<object>(),
                lojas = Array.Empty<object>(),
            };
            var json = JsonSerializer.Serialize(new { data = tenant });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
