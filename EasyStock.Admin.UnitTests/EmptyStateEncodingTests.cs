using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// BUG-004 (#463): empty-states que mostram "?empresaId=&lt;guid&gt;" sofriam
/// double-encoding (a fonte vinha pre-escapada e o TagHelper re-encodava), exibindo
/// "&amp;lt;guid&amp;gt;" cru ao usuario. Renderiza as paginas reais (OnGet vazio,
/// sem API) e assere o HTML emitido pelo servidor. Reusa a factory de XssRenderTests.
/// </summary>
public class EmptyStateEncodingTests : IClassFixture<XssRenderTests.AdminFactory>
{
    private readonly XssRenderTests.AdminFactory _factory;
    public EmptyStateEncodingTests(XssRenderTests.AdminFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/Dispositivos")]
    [InlineData("/Operacao")]
    public async Task EmptyState_sem_entidade_html_dupla_escapada(string rota)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync(rota);
        var html = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "render falhou. body: {0}",
            html.Length > 1500 ? html[..1500] : html);

        // Sem double-encoding: "&amp;lt;" exibiria "&lt;" cru ao usuario.
        html.Should().NotContain("&amp;lt;");
        html.Should().NotContain("&amp;gt;");

        // O texto de ajuda mostra "<guid>" (encodado UMA vez = "&lt;guid&gt;" no HTML).
        html.Should().Contain("empresaId=&lt;guid&gt;");
    }
}
