using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ConfiguracoesLoja;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class ConfiguracaoLojaUseCaseTests
{
    [Fact]
    public async Task Atualizar_DeveAplicarPatchParcialSemPerderValoresOriginais()
    {
        var lojaRepository = Substitute.For<ILojaRepository>();
        var configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<AtualizarConfiguracaoLojaUseCase>>();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var loja = new Loja { Id = lojaId, EmpresaId = empresaId, Nome = "Loja 1", Ativa = true };
        var configuracao = ConfiguracaoLoja.CriarPadrao(lojaId);
        configuracao.NotificarParado = false;

        lojaRepository.GetByIdAsync(empresaId, lojaId).Returns(loja);
        configuracaoRepository.GetByLojaIdAsync(lojaId).Returns(configuracao);

        var useCase = new AtualizarConfiguracaoLojaUseCase(lojaRepository, configuracaoRepository, unitOfWork, logger);

        var result = await useCase.ExecuteAsync(new AtualizarConfiguracaoLojaCommand(
            empresaId,
            lojaId,
            DiasAlertaValidade: 7,
            DiasAlertaParado: null,
            QuantidadeMinimaPadrao: 12,
            QuantidadeCriticaPadrao: null,
            NotificarEstoqueCritico: null,
            NotificarValidade: false,
            NotificarParado: null,
            NotificarReposicao: null,
            FifoAtivo: null,
            Moeda: "USD",
            Timezone: null));

        result.DiasAlertaValidade.Should().Be(7);
        result.QuantidadeMinimaPadrao.Should().Be(12);
        result.NotificarValidade.Should().BeFalse();
        result.NotificarParado.Should().BeFalse();
        result.DiasAlertaParado.Should().Be(30);
        result.Moeda.Should().Be("USD");
        result.Timezone.Should().Be("America/Sao_Paulo");
        await configuracaoRepository.Received(1).UpdateAsync(Arg.Any<ConfiguracaoLoja>());
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Resetar_DeveVoltarParaDefaultsDoSistema()
    {
        var lojaRepository = Substitute.For<ILojaRepository>();
        var configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<ResetarConfiguracaoLojaUseCase>>();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var loja = new Loja { Id = lojaId, EmpresaId = empresaId, Nome = "Loja 1", Ativa = true };
        var configuracao = ConfiguracaoLoja.CriarPadrao(lojaId);
        configuracao.Atualizar(5, 10, 2, false, false, false, false, false, "USD", "UTC");

        lojaRepository.GetByIdAsync(empresaId, lojaId).Returns(loja);
        configuracaoRepository.GetByLojaIdAsync(lojaId).Returns(configuracao);

        var useCase = new ResetarConfiguracaoLojaUseCase(lojaRepository, configuracaoRepository, unitOfWork, logger);

        var result = await useCase.ExecuteAsync(new ResetarConfiguracaoLojaCommand(empresaId, lojaId));

        result.DiasAlertaValidade.Should().Be(15);
        result.DiasAlertaParado.Should().Be(30);
        result.QuantidadeMinimaPadrao.Should().Be(5);
        result.NotificarEstoqueCritico.Should().BeTrue();
        result.NotificarValidade.Should().BeTrue();
        result.NotificarParado.Should().BeTrue();
        result.NotificarReposicao.Should().BeTrue();
        result.FifoAtivo.Should().BeTrue();
        result.Moeda.Should().Be("BRL");
        result.Timezone.Should().Be("America/Sao_Paulo");
        await configuracaoRepository.Received(1).UpdateAsync(Arg.Any<ConfiguracaoLoja>());
        await unitOfWork.Received(1).CommitAsync();
    }

    [Theory]
    [InlineData(8, 5)]    // R6: ambos no comando, critico >= minimo
    [InlineData(5, null)] // R6: patch parcial, critico(5) >= minimo persistido(5)
    public async Task Atualizar_DeveBloquear_QuandoCriticoMaiorOuIgualAoMinimo(int critico, int? minimo)
    {
        // R6 (ADR-0039): config nao pode salvar nivel critico >= minimo.
        var lojaRepository = Substitute.For<ILojaRepository>();
        var configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<AtualizarConfiguracaoLojaUseCase>>();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var loja = new Loja { Id = lojaId, EmpresaId = empresaId, Nome = "Loja 1", Ativa = true };
        var configuracao = ConfiguracaoLoja.CriarPadrao(lojaId); // minima 5, critica 2

        lojaRepository.GetByIdAsync(empresaId, lojaId).Returns(loja);
        configuracaoRepository.GetByLojaIdAsync(lojaId).Returns(configuracao);

        var useCase = new AtualizarConfiguracaoLojaUseCase(lojaRepository, configuracaoRepository, unitOfWork, logger);

        var act = async () => await useCase.ExecuteAsync(new AtualizarConfiguracaoLojaCommand(
            empresaId,
            lojaId,
            DiasAlertaValidade: null,
            DiasAlertaParado: null,
            QuantidadeMinimaPadrao: minimo,
            QuantidadeCriticaPadrao: critico,
            NotificarEstoqueCritico: null,
            NotificarValidade: null,
            NotificarParado: null,
            NotificarReposicao: null,
            FifoAtivo: null,
            Moeda: null,
            Timezone: null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await unitOfWork.DidNotReceive().CommitAsync();
    }
}
