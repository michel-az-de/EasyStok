using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.AlertasDias;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Application.UseCases.Inteligencia.ItensParados;
using EasyStock.Application.UseCases.Inteligencia.ProximoVencimento;
using EasyStock.Domain.Reposicao;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Analytics;

/// <summary>
/// #2458: <c>configuracoes_loja</c> não tem <c>EmpresaId</c>, filtro de tenant nem RLS. Quem lê a configuração por um
/// <c>lojaId</c> vindo da query precisa amarrar a empresa, senão a loja de outra empresa empresta os limiares dela.
/// O repositório simula o vazamento: pelo id puro a loja alheia responde; pela empresa e loja, não.
/// </summary>
public class ConfiguracaoLojaDeOutraEmpresaTests
{
    private const int DiasValidadePadrao = 15;
    private const int DiasParadoPadrao = 90;

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaAlheia = Guid.NewGuid();
    private readonly IConfiguracaoLojaRepository _configuracoes = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();
    private readonly IAnalyticsRepository _analytics = Substitute.For<IAnalyticsRepository>();
    private readonly IEasyStockConfiguracoes _config = Substitute.For<IEasyStockConfiguracoes>();

    public ConfiguracaoLojaDeOutraEmpresaTests()
    {
        _config.DiasAlertaVencimento.Returns(DiasValidadePadrao);
        _config.DiasItemParado.Returns(DiasParadoPadrao);

        var daOutraEmpresa = ConfiguracaoLoja.CriarPadrao(_lojaAlheia);
        daOutraEmpresa.DiasAlertaValidade = 99;
        daOutraEmpresa.DiasAlertaParado = 99;
        daOutraEmpresa.LeadTimePadraoDias = 99;
        _configuracoes.GetByLojaIdAsync(_lojaAlheia).Returns(daOutraEmpresa);
        _configuracoes.GetOrDefaultAsync(_lojaAlheia).Returns(daOutraEmpresa);
        _configuracoes.GetDaLojaAsync(_empresaId, _lojaAlheia, Arg.Any<CancellationToken>())
            .Returns((ConfiguracaoLoja?)null);

        _itens.GetItensParadosAsync(default, default, default, default, default)
            .ReturnsForAnyArgs((Enumerable.Empty<ItemEstoque>(), 0));
        _itens.GetProximoVencimentoAsync(default, default, default, default, default)
            .ReturnsForAnyArgs((Enumerable.Empty<ItemEstoque>(), 0));
        _analytics.GetSnapshotReposicaoAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(new List<ProdutoReposicaoSnapshot>());
    }

    [Fact]
    public async Task DiasAlertaValidade_LojaDeOutraEmpresa_CaiNoPadrao()
    {
        var useCase = new ObterDiasAlertaValidadeUseCase(
            _configuracoes, _config, NullLogger<ObterDiasAlertaValidadeUseCase>.Instance);

        var result = await useCase.ExecuteAsync(new ObterDiasAlertaValidadeCommand(_empresaId, _lojaAlheia));

        result.Dias.Should().Be(DiasValidadePadrao);
    }

    [Fact]
    public async Task DiasAlertaParado_LojaDeOutraEmpresa_CaiNoPadrao()
    {
        var useCase = new ObterDiasAlertaParadoUseCase(
            _configuracoes, _config, NullLogger<ObterDiasAlertaParadoUseCase>.Instance);

        var result = await useCase.ExecuteAsync(new ObterDiasAlertaParadoCommand(_empresaId, _lojaAlheia));

        result.Dias.Should().Be(DiasParadoPadrao);
    }

    [Fact]
    public async Task ItensParados_LojaDeOutraEmpresa_UsaDiasPadrao()
    {
        var useCase = new ObterItensParadosUseCase(
            _itens, _configuracoes, _config, NullLogger<ObterItensParadosUseCase>.Instance);

        await useCase.ExecuteAsync(new ObterItensParadosCommand(_empresaId, _lojaAlheia));

        await _itens.Received(1).GetItensParadosAsync(_empresaId, DiasParadoPadrao, 1, 20, _lojaAlheia);
    }

    [Fact]
    public async Task ProximoVencimento_LojaDeOutraEmpresa_UsaDiasPadrao()
    {
        var useCase = new ObterProximoVencimentoUseCase(
            _itens, _configuracoes, _config, NullLogger<ObterProximoVencimentoUseCase>.Instance);

        await useCase.ExecuteAsync(new ObterProximoVencimentoCommand(_empresaId, _lojaAlheia));

        await _itens.Received(1).GetProximoVencimentoAsync(_empresaId, DiasValidadePadrao, 1, 20, _lojaAlheia);
    }

    [Fact]
    public async Task Reposicao_LojaDeOutraEmpresa_NaoUsaLeadTimeDela()
    {
        var useCase = new ObterReposicaoUseCase(_analytics, _configuracoes, NullLogger<ObterReposicaoUseCase>.Instance);

        await useCase.ExecuteAsync(new ObterReposicaoCommand(_empresaId, _lojaAlheia));

        await _analytics.DidNotReceive().GetSnapshotReposicaoAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), 99,
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiasAlertaValidade_LojaDaPropriaEmpresa_UsaOsDiasDela()
    {
        var lojaPropria = Guid.NewGuid();
        var propria = ConfiguracaoLoja.CriarPadrao(lojaPropria);
        propria.DiasAlertaValidade = 7;
        _configuracoes.GetDaLojaAsync(_empresaId, lojaPropria, Arg.Any<CancellationToken>()).Returns(propria);
        var useCase = new ObterDiasAlertaValidadeUseCase(
            _configuracoes, _config, NullLogger<ObterDiasAlertaValidadeUseCase>.Instance);

        var result = await useCase.ExecuteAsync(new ObterDiasAlertaValidadeCommand(_empresaId, lojaPropria));

        result.Dias.Should().Be(7);
    }
}
