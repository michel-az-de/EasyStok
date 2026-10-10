using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers.Storefront;

/// <summary>
/// #1508: o cookie __Host-cdb_aval_* saía com Path=/avaliar. O prefixo __Host- exige Path=/, o navegador
/// descartava o cookie e todo POST /api/storefront/{slug}/avaliacoes respondia 401.
/// </summary>
public class AvaliacaoControllerTests
{
    [Fact]
    public async Task AbrirPagina_EmiteCookieHostComPathRaiz()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Avaliacao:JwtSecret"] = "segredo-de-teste-da-avaliacao-com-32-chars",
        }).Build();
        var tokens = new AvaliacaoTokenService(config, TimeProvider.System);
        var abrir = new AbrirPaginaAvaliacaoUseCase(tokens, new AvaliacaoCookieStore(Substitute.For<ICacheService>()));
        var controller = new AvaliacaoController(abrir, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var pedidoId = Guid.NewGuid();

        var result = await controller.AbrirPaginaAvaliacao(pedidoId, tokens.Gerar(pedidoId));

        result.Should().BeOfType<RedirectResult>();
        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().StartWith($"__Host-cdb_aval_{pedidoId}=");
        setCookie.Should().Contain("path=/;").And.NotContain("/avaliar");
        setCookie.Should().Contain("secure").And.Contain("httponly");
    }
}
