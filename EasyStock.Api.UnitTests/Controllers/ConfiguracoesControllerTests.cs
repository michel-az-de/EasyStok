using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ConfiguracoesLoja;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class ConfiguracoesControllerTests
{
    private readonly ILojaRepository _lojaRepository = Substitute.For<ILojaRepository>();
    private readonly IConfiguracaoLojaRepository _configuracaoLojaRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ConfiguracoesController _controller;

    public ConfiguracoesControllerTests()
    {
        var obter = new ObterConfiguracaoLojaUseCase(_lojaRepository, _configuracaoLojaRepository);
        var atualizarLogger = Substitute.For<ILogger<AtualizarConfiguracaoLojaUseCase>>();
        var resetarLogger = Substitute.For<ILogger<ResetarConfiguracaoLojaUseCase>>();
        var atualizar = new AtualizarConfiguracaoLojaUseCase(_lojaRepository, _configuracaoLojaRepository, _unitOfWork, atualizarLogger);
        var resetar = new ResetarConfiguracaoLojaUseCase(_lojaRepository, _configuracaoLojaRepository, _unitOfWork, resetarLogger);
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _controller = new ConfiguracoesController(obter, atualizar, resetar, _currentUser);
    }

    [Fact]
    public async Task Patch_DeveRetornarConfiguracaoAtualizada()
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        _lojaRepository.GetByIdAsync(empresaId, lojaId).Returns(new Domain.Entities.Loja { Id = lojaId, EmpresaId = empresaId, Nome = "Loja", Ativa = true });
        _configuracaoLojaRepository.GetByLojaIdAsync(lojaId).Returns(Domain.Entities.ConfiguracaoLoja.CriarPadrao(lojaId));

        var result = await _controller.Patch(new AtualizarConfiguracaoLojaCommand(
            empresaId,
            lojaId,
            7,
            null,
            9,
            null,
            null,
            null,
            false,
            null,
            null,
            "USD",
            null));

        result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<ConfiguracaoLojaResult>>().Subject.Data;
        payload.DiasAlertaValidade.Should().Be(7);
        payload.QuantidadeMinimaPadrao.Should().Be(9);
        payload.NotificarParado.Should().BeFalse();
        payload.Moeda.Should().Be("USD");
    }

    [Fact]
    public async Task Reset_DeveRetornarConfiguracaoPadrao()
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var configuracao = Domain.Entities.ConfiguracaoLoja.CriarPadrao(lojaId);
        configuracao.Atualizar(2, 3, 1, false, false, false, false, false, "USD", "UTC");
        _lojaRepository.GetByIdAsync(empresaId, lojaId).Returns(new Domain.Entities.Loja { Id = lojaId, EmpresaId = empresaId, Nome = "Loja", Ativa = true });
        _configuracaoLojaRepository.GetByLojaIdAsync(lojaId).Returns(configuracao);

        var result = await _controller.Reset(new ResetarConfiguracaoLojaCommand(empresaId, lojaId));

        result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<ConfiguracaoLojaResult>>().Subject.Data;
        payload.DiasAlertaValidade.Should().Be(15);
        payload.DiasAlertaParado.Should().Be(30);
        payload.QuantidadeMinimaPadrao.Should().Be(5);
        payload.Moeda.Should().Be("BRL");
        payload.Timezone.Should().Be("America/Sao_Paulo");
    }
}
