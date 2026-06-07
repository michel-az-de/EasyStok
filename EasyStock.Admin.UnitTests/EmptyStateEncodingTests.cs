using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// BUG-004 (#463): empty-states sofriam double-encoding (fonte pre-escapada + o
/// TagHelper re-encodava). O hint tecnico de empresaId na URL foi removido depois
/// em bbb9a33d (empty-state virou o CTA "Selecione um cliente"); o teste segue
/// guardando que o HTML servidor-side nao tem double-encoding e que o empty-state
/// renderiza. Renderiza as paginas reais (OnGet vazio, sem API) e assere o HTML
/// emitido pelo servidor. Reusa a factory de XssRenderTests.
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

        // Empty-state (sem empresaId) renderiza o CTA amigavel introduzido em bbb9a33d.
        html.Should().Contain("Selecione um cliente");
    }
}
