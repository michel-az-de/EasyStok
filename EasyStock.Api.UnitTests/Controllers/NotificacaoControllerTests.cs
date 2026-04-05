using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
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
    public async Task GetBadge_DeveRetornarEnvelope_ComCountDeNaoLidas()
    {
        var empresaId = Guid.NewGuid();
        _notificacaoRepository.CountNaoLidasAsync(empresaId).Returns(4);

        var result = await _controller.GetBadge(empresaId);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        // badge fica em data.count (camelCase)
        var countProp = envelope.Data.GetType().GetProperty("count");
        countProp.Should().NotBeNull();
        countProp!.GetValue(envelope.Data).Should().Be(4);
    }

    [Fact]
    public async Task Delete_DeveRetornarNotFoundObjectResult_QuandoNaoExistir()
    {
        _notificacaoRepository.GetByIdAsync(Arg.Any<Guid>()).Returns((Notificacao?)null);

        var result = await _controller.Delete(Guid.NewGuid());

        // Novo contrato: NotFoundObjectResult com { error: { code: "NOT_FOUND" } }
        result.Should().BeOfType<NotFoundObjectResult>();
        var notFound = (NotFoundObjectResult)result;
        var envelope = notFound.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        envelope.Error.Code.Should().Be("NOT_FOUND");
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

    [Fact]
    public async Task GetAll_DeveRetornarPagedEnvelope()
    {
        var empresaId = Guid.NewGuid();
        var notificacoes = new List<Notificacao>
        {
            new() { Id = Guid.NewGuid(), EmpresaId = empresaId, Mensagem = "Estoque baixo", TipoAlerta = TipoAlertaEstoque.EstoqueBaixo }
        };
        _notificacaoRepository.GetByEmpresaAsync(empresaId, null, null, 1, 20).Returns((notificacoes, 1));

        var result = await _controller.GetAll(empresaId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<Notificacao>>>().Subject;
        var meta = envelope.Meta.Should().BeOfType<PagedMeta>().Subject;
        meta.Total.Should().Be(1);
        meta.Pages.Should().Be(1);
        meta.Page.Should().Be(1);
        meta.Limit.Should().Be(20);
    }
}
