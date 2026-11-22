using System.Text.Encodings.Web;
using EasyStock.Api.Authentication;
using EasyStock.Api.Middleware;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Middleware;

/// <summary>
/// #3096 (ADR-0013): a sessão do storefront roda em toda requisição. A leitura herda o cancelamento do
/// cliente; a renovação pós-resposta não pode ser cancelada por ele.
/// </summary>
public class ClienteSessionCancelamentoTests : IDisposable
{
    private readonly IClienteSessionRepository _repo = Substitute.For<IClienteSessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly CancellationTokenSource _cts = new();

    private DefaultHttpContext Ctx()
    {
        var services = new ServiceCollection()
            .AddSingleton(_repo).AddSingleton(_uow).AddSingleton(_time).BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services, RequestAborted = _cts.Token };
    }

    public void Dispose() => _cts.Dispose();

    private ClienteSession Sessao() => ClienteSession.Criar(Guid.NewGuid(), Guid.NewGuid(), _time);

    [Fact]
    public async Task Middleware_le_com_o_token_da_requisicao_e_renova_a_sessao()
    {
        var sessao = Sessao();
        _repo.GetByIdAsync(sessao.Id, Arg.Any<CancellationToken>()).Returns(sessao);
        var ctx = Ctx();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sid", sessao.Id.ToString())], ClienteSessionAuthenticationHandler.SchemeName));

        await new ClienteSessionMiddleware(_ => Task.CompletedTask).InvokeAsync(ctx);

        await _repo.Received(1).GetByIdAsync(sessao.Id, _cts.Token);
        // A renovação não herda o cancelamento do cliente: token não cancelável (ADR-0013).
        await _repo.Received(1).UpdateAsync(sessao, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Handler_le_com_o_token_da_requisicao()
    {
        var sessao = Sessao();
        _repo.GetByIdAsync(sessao.Id, Arg.Any<CancellationToken>()).Returns(sessao);
        var ctx = Ctx();
        ctx.Request.Headers.Cookie = $"{ClienteSessionAuthenticationHandler.CookieName}={sessao.Id}";
        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var handler = new ClienteSessionAuthenticationHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default);
        await handler.InitializeAsync(
            new AuthenticationScheme(ClienteSessionAuthenticationHandler.SchemeName, null, typeof(ClienteSessionAuthenticationHandler)), ctx);

        await handler.AuthenticateAsync();

        await _repo.Received(1).GetByIdAsync(sessao.Id, _cts.Token);
    }

    [Fact]
    public async Task Middleware_com_pedido_cancelado_propaga_o_cancelamento_e_nao_grava()
    {
        var sessao = Sessao();
        _cts.Cancel();
        _repo.GetByIdAsync(sessao.Id, _cts.Token).Returns<ClienteSession?>(_ => throw new OperationCanceledException(_cts.Token));
        var ctx = Ctx();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sid", sessao.Id.ToString())], ClienteSessionAuthenticationHandler.SchemeName));

        var acao = () => new ClienteSessionMiddleware(_ => Task.CompletedTask).InvokeAsync(ctx);

        await acao.Should().ThrowAsync<OperationCanceledException>();
        await _repo.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Handler_com_pedido_cancelado_propaga_o_cancelamento_e_nao_autentica()
    {
        var sessao = Sessao();
        _cts.Cancel();
        _repo.GetByIdAsync(sessao.Id, _cts.Token).Returns<ClienteSession?>(_ => throw new OperationCanceledException(_cts.Token));
        var ctx = Ctx();
        ctx.Request.Headers.Cookie = $"{ClienteSessionAuthenticationHandler.CookieName}={sessao.Id}";
        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var handler = new ClienteSessionAuthenticationHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default);
        await handler.InitializeAsync(
            new AuthenticationScheme(ClienteSessionAuthenticationHandler.SchemeName, null, typeof(ClienteSessionAuthenticationHandler)), ctx);

        AuthenticateResult? resultado = null;
        var acao = async () => resultado = await handler.AuthenticateAsync();

        await acao.Should().ThrowAsync<OperationCanceledException>();
        resultado.Should().BeNull("o cancelamento não pode virar Success");
    }
}
