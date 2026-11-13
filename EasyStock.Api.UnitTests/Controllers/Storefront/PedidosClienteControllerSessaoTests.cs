using System.Text;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers.Storefront;

/// <summary>
/// #2370: o cookie de sessão do storefront é o Guid da ClienteSession (AuthController). Um texto com cara de JWT
/// não é decodificado sem assinatura para virar sid; é recusado antes de consultar a sessão.
/// </summary>
public sealed class PedidosClienteControllerSessaoTests
{
    private static PedidosClienteController Criar(IClienteSessionRepository sessoes, string cookie)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"__Host-cdb_session={cookie}";
        return new PedidosClienteController(null!, null!, null!, null!, sessoes, TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=');

    [Fact]
    public async Task Listar_CookieComCaraDeJwt_401SemConsultarSessao()
    {
        var sessoes = Substitute.For<IClienteSessionRepository>();
        var jwt = $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"sid\":\"{Guid.NewGuid()}\"}}")}.";

        var resultado = await Criar(sessoes, jwt).Listar("loja", null, CancellationToken.None);

        resultado.Should().BeOfType<UnauthorizedObjectResult>();
        await sessoes.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }
}
