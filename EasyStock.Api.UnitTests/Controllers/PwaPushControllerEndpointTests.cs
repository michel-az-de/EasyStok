using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Infra.Notifications.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #1508: o endpoint da inscrição aceitava qualquer URL (SSRF cego pelo worker).
/// </summary>
public class PwaPushControllerEndpointTests
{
    private const string EndpointFcm = "https://fcm.googleapis.com/fcm/send/abc";

    private readonly IWebPushSubscriptionRepository _repo = Substitute.For<IWebPushSubscriptionRepository>();
    private readonly ICurrentUserAccessor _usuario = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly Guid _empresaId = Guid.NewGuid();

    private PwaPushController Controller()
    {
        _usuario.UsuarioId.Returns(_usuarioId);
        _usuario.EmpresaId.Returns(_empresaId);
        return new PwaPushController(_repo, _usuario, Options.Create(new WebPushOptions()));
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("https://intranet.local/push")]
    public async Task Subscribe_endpoint_fora_da_allowlist_retorna_400_sem_gravar(string endpoint)
    {
        var result = await Controller().Subscribe(
            new PwaPushController.SubscribeRequest(endpoint, "p256dh", "auth", null), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _repo.DidNotReceiveWithAnyArgs().GetByEndpointAsync(default!, default);
    }

    [Fact]
    public async Task Subscribe_endpoint_de_servico_de_push_grava()
    {
        var result = await Controller().Subscribe(
            new PwaPushController.SubscribeRequest(EndpointFcm, "p256dh", "auth", null), CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();
        await _repo.Received(1).AddAsync(Arg.Is<WebPushSubscription>(s => s.Endpoint == EndpointFcm), Arg.Any<CancellationToken>());
    }
}
