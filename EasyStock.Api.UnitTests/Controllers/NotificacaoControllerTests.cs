using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
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
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly NotificacaoController _controller;

    public NotificacaoControllerTests()
    {
        _currentUser.UsuarioId.Returns(Guid.NewGuid());
        _controller = new NotificacaoController(_notificacaoRepository, _unitOfWork, _currentUser);
    }

    [Fact]
    public async Task GetBadge_DeveRetornarEnvelope_ComCountDeNaoLidas()
    {
        var empresaId = Guid.NewGuid();
        _notificacaoRepository.CountNaoLidasAsync(empresaId, _currentUser.UsuarioId).Returns(4);

        var result = await _controller.GetBadge(empresaId);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().NotBeNull();
        var dataProp = ok.Value!.GetType().GetProperty("Data");
        dataProp.Should().NotBeNull();
        var data = dataProp!.GetValue(ok.Value);
        var countProp = data!.GetType().GetProperty("count");
        countProp.Should().NotBeNull($"propriedade 'count' nao encontrada");
        countProp!.GetValue(data).Should().Be(4);
    }

    [Fact]
    public async Task Delete_DeveRetornarNotFoundObjectResult_QuandoNaoExistir()
    {
        _notificacaoRepository.GetByIdAsync(Arg.Any<Guid>()).Returns((Notificacao?)null);

        var result = await _controller.Delete(Guid.NewGuid(), Guid.NewGuid());

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
        await _notificacaoRepository.Received(1).MarcarTodasComoLidasAsync(empresaId, _currentUser.UsuarioId);
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
        _notificacaoRepository.GetByEmpresaAsync(empresaId, null, null, null, 1, 20, _currentUser.UsuarioId).Returns((notificacoes, 1));

        var result = await _controller.GetAll(empresaId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
        var metaProp = ok.Value!.GetType().GetProperty("Meta");
        metaProp.Should().NotBeNull("o envelope deve ter propriedade Meta");
        var meta = metaProp!.GetValue(ok.Value).Should().BeOfType<PagedMeta>().Subject;
        meta.Total.Should().Be(1);
        meta.Pages.Should().Be(1);
        meta.Page.Should().Be(1);
        meta.Limit.Should().Be(20);
    }

    [Fact]
    public async Task RecentesEResumo_UsamDestinatarioDoJwtELimitamQuantidade()
    {
        var empresa = Guid.NewGuid();
        _notificacaoRepository.GetRecentesNaoLidasAsync(empresa, 1, _currentUser.UsuarioId).Returns([]);
        await _controller.GetRecentes(empresa, -1);
        await _controller.GetResumo(empresa);
        await _notificacaoRepository.Received(1).GetRecentesNaoLidasAsync(empresa, 1, _currentUser.UsuarioId);
        await _notificacaoRepository.Received(1).GetResumoAsync(empresa, _currentUser.UsuarioId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AvisoDeOutroDestinatario_NaoPodeSerLidoNemExcluido(bool outraEmpresa)
    {
        var empresa = Guid.NewGuid();
        var aviso = new Notificacao { Id = Guid.NewGuid(), EmpresaId = outraEmpresa ? Guid.NewGuid() : empresa,
            UsuarioId = Guid.NewGuid() };
        _notificacaoRepository.GetByIdAsync(aviso.Id).Returns(aviso);
        (await _controller.MarcarLida(aviso.Id, empresa)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.Delete(aviso.Id, empresa)).Should().BeOfType<NotFoundObjectResult>();
        aviso.Lida.Should().BeFalse();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }
}
