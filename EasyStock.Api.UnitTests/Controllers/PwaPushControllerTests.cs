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

public class PwaPushControllerTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly IWebPushSubscriptionRepository _repo = Substitute.For<IWebPushSubscriptionRepository>();
    private readonly ICurrentUserAccessor _sessao = Substitute.For<ICurrentUserAccessor>();
    private readonly PwaPushController _controller;
    private static readonly PwaPushController.SubscribeRequest Inscricao = new("https://fcm.googleapis.com/fcm/send/aviso", /* #1508: so servico de push */ "chave", "segredo", "teste");

    public PwaPushControllerTests()
    {
        _sessao.EmpresaId.Returns(_empresa);
        _sessao.UsuarioId.Returns(_usuario);
        _controller = new PwaPushController(_repo, _sessao, Options.Create(new WebPushOptions()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InscreverOuDesativar_RecusaEndpointDeOutraConta(bool outraEmpresa)
    {
        var anterior = WebPushSubscription.Criar(Inscricao.Endpoint, "original", "original",
            outraEmpresa ? Guid.NewGuid() : _empresa, outraEmpresa ? _usuario : Guid.NewGuid());
        _repo.GetByEndpointAsync(Inscricao.Endpoint, default).Returns(anterior);

        (await _controller.Subscribe(Inscricao, default)).Should().BeOfType<BadRequestObjectResult>();
        (await _controller.Unsubscribe(Inscricao.Endpoint, default)).Should().BeOfType<NotFoundObjectResult>();

        anterior.P256dh.Should().Be("original");
        anterior.Ativo.Should().BeTrue();
        await _repo.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _repo.DidNotReceiveWithAnyArgs().DesativarAsync(default!, default);
    }

    [Fact]
    public async Task Inscrever_UsaEmpresaEUsuarioDaSessao()
    {
        (await _controller.Subscribe(Inscricao, default)).Should().BeOfType<CreatedResult>();
        await _repo.Received(1).AddAsync(Arg.Is<WebPushSubscription>(s => s.EmpresaId == _empresa && s.UsuarioId == _usuario), default);
    }

    [Fact]
    public async Task MesmaConta_ReinscreveEDesativa()
    {
        var anterior = WebPushSubscription.Criar(Inscricao.Endpoint, "antiga", "antigo", _empresa, _usuario);
        anterior.Desativar();
        _repo.GetByEndpointAsync(Inscricao.Endpoint, default).Returns(anterior);
        (await _controller.Subscribe(Inscricao, default)).Should().BeOfType<OkObjectResult>();
        anterior.P256dh.Should().Be(Inscricao.P256dh);
        anterior.Ativo.Should().BeTrue();
        (await _controller.Unsubscribe(Inscricao.Endpoint, default)).Should().BeOfType<NoContentResult>();
        await _repo.Received(1).DesativarAsync(Inscricao.Endpoint, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inscrever_SemEmpresaOuUsuario_Recusa(bool semEmpresa)
    {
        if (semEmpresa) _sessao.EmpresaId.Returns(Guid.Empty);
        else _sessao.UsuarioId.Returns(Guid.Empty);
        (await _controller.Subscribe(Inscricao, default)).Should().BeOfType<BadRequestObjectResult>();
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
