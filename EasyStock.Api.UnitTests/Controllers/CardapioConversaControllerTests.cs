using System.Reflection;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Domain.Entities.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Cardápio da conversa (S48) na borda HTTP: link vencido ou já usado é 410 com a orientação de pedir
/// um link novo na conversa; o endpoint é anônimo e usa o rate limit <c>public-post</c>.
/// </summary>
public class CardapioConversaControllerTests
{
    private static readonly DateTime Agora = DateTime.UtcNow;
    private readonly ILinkCardapioConversaRepository _links = Substitute.For<ILinkCardapioConversaRepository>();
    private readonly CardapioConversaController _controller;

    public CardapioConversaControllerTests()
    {
        var linkService = new LinkCardapioConversaService(_links,
            new SaudacaoAtendimento(Substitute.For<IStorefrontRepository>(), new ConfigurationBuilder().Build()));
        // Criar pedido e cobrar não são alcançados: o token é recusado antes.
        var useCase = new CriarPedidoPeloCardapioConversaUseCase(
            linkService, Substitute.For<ITenantContextAccessor>(), Substitute.For<IConversaRepository>(),
            Substitute.For<IClienteRepository>(), null!, null!, null!, Substitute.For<IOperacaoEventPublisher>(),
            Substitute.For<IUnitOfWork>(), TimeProvider.System,
            NullLogger<CriarPedidoPeloCardapioConversaUseCase>.Instance, null!);
        _controller = new CardapioConversaController(useCase)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static PedidoCardapioConversaRequestBody Corpo() =>
        new([new ItemCardapioConversaRequest(Guid.NewGuid(), 1)], Guid.NewGuid(), DateOnly.FromDateTime(Agora.AddDays(1)));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TokenVencidoOuUsado_410ComOrientacao(bool usado, bool vencido)
    {
        const string token = "token-do-link";
        var link = LinkCardapioConversa.Gerar(Guid.NewGuid(), Guid.NewGuid(), AcessoChatSite.HashDoToken(token),
            vencido ? Agora.AddDays(-2) : Agora);
        if (usado) link.Consumir(Agora);
        _links.ObterPorTokenHashAsync(AcessoChatSite.HashDoToken(token), Arg.Any<CancellationToken>()).Returns(link);

        var result = await _controller.EnviarPedido(token, Corpo(), default);

        var objeto = result.Should().BeOfType<ObjectResult>().Subject;
        objeto.StatusCode.Should().Be(StatusCodes.Status410Gone);
        objeto.Value.Should().BeOfType<ProblemDetails>().Which.Detail
            .Should().Be(LinkCardapioConversaIndisponivelException.Orientacao).And.Contain("link novo na conversa");
    }

    [Fact]
    public async Task CarrinhoVazio_400()
    {
        var result = await _controller.EnviarPedido("qualquer", Corpo() with { Itens = [] }, default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Anonimo_ComRateLimitPublicPost()
    {
        typeof(CardapioConversaController).GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        typeof(CardapioConversaController).GetCustomAttribute<RouteAttribute>()!.Template
            .Should().Be("api/storefront/cardapio-conversa/{token}");
        var metodo = typeof(CardapioConversaController).GetMethod(nameof(CardapioConversaController.EnviarPedido))!;
        metodo.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("public-post");
        metodo.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("pedido");
    }
}
