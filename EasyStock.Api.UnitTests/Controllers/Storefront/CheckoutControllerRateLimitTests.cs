using EasyStock.Api.Controllers.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.UnitTests.Controllers.Storefront;

/// <summary>
/// #1508: o checkout guest é anônimo e cria pedido; ganha o mesmo teto do outro POST público que cria
/// pedido (cardápio da conversa). O checkout com sessão segue sem limitador próprio.
/// </summary>
public class CheckoutControllerRateLimitTests
{
    private static string[] Policies(string acao) =>
        typeof(CheckoutController).GetMethod(acao)!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>().Select(a => a.PolicyName!).ToArray();

    [Fact]
    public void CheckoutGuest_TemRateLimitPublico() =>
        Policies(nameof(CheckoutController.IniciarCheckoutGuest)).Should().Equal("public-post");

    [Fact]
    public void CheckoutComSessao_NaoGanhaLimitadorProprio() =>
        Policies(nameof(CheckoutController.IniciarCheckout)).Should().BeEmpty();
}
