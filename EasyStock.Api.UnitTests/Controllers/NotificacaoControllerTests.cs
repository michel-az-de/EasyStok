using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class NotificacaoControllerTests
{
    private readonly INotificacaoRepository _notificacaoRepository = Substitute.For<INotificacaoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly NotificacaoController _controller;

    public NotificacaoControllerTests()
    {
        _controller = new NotificacaoController(_notificacaoRepository, _unitOfWork);
    }

    [Fact]
    public async Task GetBadge_DeveRetornarCountDeNaoLidas()
    {
        var empresaId = Guid.NewGuid();
        _notificacaoRepository.CountNaoLidasAsync(empresaId).Returns(4);

        var result = await _controller.GetBadge(empresaId);

        result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)result).Value!;
        payload.GetType().GetProperty("Count")!.GetValue(payload).Should().Be(4);
    }

    [Fact]
    public async Task Delete_DeveRetornarNotFound_QuandoNaoExistir()
    {
        _notificacaoRepository.GetByIdAsync(Arg.Any<Guid>()).Returns((Notificacao?)null);

        var result = await _controller.Delete(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task MarcarTodasLidas_DevePersistirCommit()
    {
        var empresaId = Guid.NewGuid();

        var result = await _controller.MarcarTodasLidas(empresaId);

        result.Should().BeOfType<NoContentResult>();
        await _notificacaoRepository.Received(1).MarcarTodasComoLidasAsync(empresaId);
        await _unitOfWork.Received(1).CommitAsync();
    }
}
