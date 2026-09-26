using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class AtendimentoConfiguracaoControllerTests
{
    private readonly IConfiguracaoAtendimentoRepository _repository = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AtendimentoConfiguracaoController _controller;
    private readonly Guid _empresaId = Guid.NewGuid();

    public AtendimentoConfiguracaoControllerTests()
    {
        var obterUseCase = new ObterConfiguracaoAtendimentoUseCase(_repository);
        var atualizarUseCase = new AtualizarConfiguracaoAtendimentoUseCase(_repository, _unitOfWork);
        _currentUser.EmpresaId.Returns(_empresaId);
        _controller = new AtendimentoConfiguracaoController(obterUseCase, atualizarUseCase, _currentUser);
    }

    [Fact]
    public async Task GetSemRegistroDevolvePadrao()
    {
        _repository.GetOrDefaultAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));

        var result = await _controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var data = (ConfiguracaoAtendimentoResult)ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value)!;
        data.RespiroMinutos.Should().Be(40);
        data.TempoPreparoPadraoMinutos.Should().Be(60);
    }

    [Fact]
    public async Task PutValida()
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);

        var body = new AtualizarConfiguracaoAtendimentoBody(
            "direto e simpático", NivelSugestaoAtendimento.Ativo,
            null, null, null, null, RespiroMinutos: 20, TempoPreparoPadraoMinutos: 90, Ativo: null);

        var result = await _controller.Put(body);

        result.Should().BeOfType<OkObjectResult>();
        await _repository.Received(1).AddAsync(Arg.Is<ConfiguracaoAtendimento>(c =>
            c.EmpresaId == _empresaId && c.Tom == "direto e simpático" &&
            c.NivelSugestao == NivelSugestaoAtendimento.Ativo &&
            c.RespiroMinutos == 20 && c.TempoPreparoPadraoMinutos == 90));
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task PutComRespiroNegativoDevolveBadRequest()
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);

        var body = new AtualizarConfiguracaoAtendimentoBody(
            null, null, null, null, null, null, RespiroMinutos: -1, TempoPreparoPadraoMinutos: null, Ativo: null);

        var result = await _controller.Put(body);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task PutComTempoPreparoZeroDevolveBadRequest()
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);

        var body = new AtualizarConfiguracaoAtendimentoBody(
            null, null, null, null, null, null, RespiroMinutos: null, TempoPreparoPadraoMinutos: 0, Ativo: null);

        var result = await _controller.Put(body);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }
}
