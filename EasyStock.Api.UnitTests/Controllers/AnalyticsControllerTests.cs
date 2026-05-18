using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.AlertasDias;
using EasyStock.Application.UseCases.Analytics.Alertas;
using EasyStock.Application.UseCases.Analytics.Dashboard;
using EasyStock.Application.UseCases.Analytics.Dia;
using EasyStock.Application.UseCases.Analytics.DashboardFull;
using EasyStock.Application.UseCases.Analytics.DashboardExtras;
using EasyStock.Application.UseCases.Analytics.Margem;
using EasyStock.Application.UseCases.Analytics.Movimentacoes;
using EasyStock.Application.UseCases.Analytics.Parados;
using EasyStock.Application.UseCases.Analytics.Projecoes;
using EasyStock.Application.UseCases.Analytics.Receita;
using EasyStock.Application.UseCases.Analytics.ReceitaCusto;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Application.UseCases.Analytics.Sazonalidade;
using EasyStock.Application.UseCases.Analytics.Validade;
using EasyStock.Application.UseCases.Analytics.VendasPorCanal;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class AnalyticsControllerTests
{
    private readonly IAnalyticsRepository _analyticsRepo = Substitute.For<IAnalyticsRepository>();
    private readonly IConfiguracaoLojaRepository _confLojaRepo = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AnalyticsController _controller;

    private static readonly Guid _empresaId = Guid.NewGuid();

    public AnalyticsControllerTests()
    {
        var config = Substitute.For<IEasyStockConfiguracoes>();
        config.DiasAlertaVencimento.Returns(30);
        config.DiasItemParado.Returns(90);

        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);

        _controller = new AnalyticsController(
            new GetDashboardUseCase(_analyticsRepo, NullLogger<GetDashboardUseCase>.Instance),
            new ObterResumoDiaUseCase(_analyticsRepo, NullLogger<ObterResumoDiaUseCase>.Instance),
            new GetDashboardFullUseCase(_analyticsRepo, NullLogger<GetDashboardFullUseCase>.Instance),
            new CalcularProjecoesUseCase(_analyticsRepo, NullLogger<CalcularProjecoesUseCase>.Instance),
            new CalcularReposicaoUseCase(_analyticsRepo, NullLogger<CalcularReposicaoUseCase>.Instance),
            new CalcularSazonalidadeUseCase(_analyticsRepo, NullLogger<CalcularSazonalidadeUseCase>.Instance),
            new ObterAlertasUseCase(_analyticsRepo, NullLogger<ObterAlertasUseCase>.Instance),
            new ObterDiasAlertaValidadeUseCase(_confLojaRepo, config, NullLogger<ObterDiasAlertaValidadeUseCase>.Instance),
            new CalcularReceitaUseCase(_analyticsRepo, NullLogger<CalcularReceitaUseCase>.Instance),
            new CalcularMargemUseCase(_analyticsRepo, NullLogger<CalcularMargemUseCase>.Instance),
            new ObterMovimentacoesUseCase(_analyticsRepo, NullLogger<ObterMovimentacoesUseCase>.Instance),
            new ObterValidadeUseCase(_analyticsRepo, NullLogger<ObterValidadeUseCase>.Instance),
            new ObterParadosUseCase(_analyticsRepo, NullLogger<ObterParadosUseCase>.Instance),
            new ObterDiasAlertaParadoUseCase(_confLojaRepo, config, NullLogger<ObterDiasAlertaParadoUseCase>.Instance),
            new ObterVendasPorCanalUseCase(_analyticsRepo, NullLogger<ObterVendasPorCanalUseCase>.Instance),
            new GetReceitaCustoUseCase(_analyticsRepo),
            new GetDashboardExtrasUseCase(_analyticsRepo),
            _currentUser);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static T OkData<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<T>>().Subject;
        return envelope.Data;
    }

    private static (IEnumerable<T> Items, PagedMeta Meta) OkPaged<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<IEnumerable<T>>>().Subject;
        var meta = envelope.Meta.Should().BeOfType<PagedMeta>().Subject;
        return (envelope.Data, meta);
    }

    private DashboardResumo MakeDashboardResumo(int totalSkus = 5, decimal receita = 1000m) =>
        new(_empresaId, 30, totalSkus, 50, 5000m, 3000m, 33m, 990m, receita, 1, 0, 2);

    // ── Dashboard ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Dashboard_DeveRetornarOk_ComResumoMapeado()
    {
        var resumo = MakeDashboardResumo(totalSkus: 10, receita: 2500m);
        _analyticsRepo.GetDashboardResumoAsync(_empresaId, 30, null).Returns(resumo);

        var result = await _controller.Dashboard(_empresaId, 30);

        var data = OkData<GetDashboardResult>(result);
        data.TotalSkus.Should().Be(10);
        data.ReceitaEstimadaPeriodo.Should().Be(2500m);
        data.EmpresaId.Should().Be(_empresaId);
    }

    [Fact]
    public async Task Dashboard_DeveChamarRepositorioComPeriodoCorreto()
    {
        _analyticsRepo.GetDashboardResumoAsync(_empresaId, 60, null)
            .Returns(MakeDashboardResumo());

        await _controller.Dashboard(_empresaId, 60);

        await _analyticsRepo.Received(1).GetDashboardResumoAsync(_empresaId, 60, null);
    }

    [Fact]
    public async Task Dashboard_DeveRetornarBadRequest_QuandoEmpresaIdVazioEhSuperAdmin()
    {
        var result = await _controller.Dashboard(Guid.Empty);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Projeções ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Projecoes_DeveRetornarOkPaginado()
    {
        var projecoes = new[]
        {
            new ProjecaoRuptura(Guid.NewGuid(), Guid.NewGuid(), "Produto A", null, 5, 1.5m, 3, DateTime.UtcNow.AddDays(3)),
            new ProjecaoRuptura(Guid.NewGuid(), Guid.NewGuid(), "Produto B", null, 0, 0m, null, null)
        };
        _analyticsRepo.GetProjecaoRupturaAsync(_empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ProjecaoRuptura>)projecoes, 2));

        var result = await _controller.Projecoes(_empresaId, 30, 1, 20);

        var (items, meta) = OkPaged<CalcularProjecoesResult>(result);
        items.Should().HaveCount(2);
        meta.Total.Should().Be(2);
    }

    [Fact]
    public async Task Projecoes_DeveChamarRepositorioComPaginacaoCorreta()
    {
        _analyticsRepo.GetProjecaoRupturaAsync(_empresaId, 30, 2, 10)
            .Returns(((IReadOnlyList<ProjecaoRuptura>)Array.Empty<ProjecaoRuptura>(), 0));

        await _controller.Projecoes(_empresaId, 30, 2, 10);

        await _analyticsRepo.Received(1).GetProjecaoRupturaAsync(_empresaId, 30, 2, 10);
    }

    // ── Sazonalidade ───────────────────────────────────────────────────────

    [Fact]
    public async Task Sazonalidade_DeveRetornarListaMensal()
    {
        var produtoId = Guid.NewGuid();
        var dados = new[]
        {
            new SazonalidadeMensal(2026, 1, 30, 1500m, 30m),
            new SazonalidadeMensal(2026, 2, 45, 2250m, 37.5m)
        };
        _analyticsRepo.GetSazonalidadeAsync(_empresaId, produtoId, 12)
            .Returns((IReadOnlyList<SazonalidadeMensal>)dados);

        var result = await _controller.Sazonalidade(_empresaId, produtoId, 12);

        result.Should().BeOfType<OkObjectResult>();
        await _analyticsRepo.Received(1).GetSazonalidadeAsync(_empresaId, produtoId, 12);
    }

    // ── Receita ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Receita_DeveRetornarListaMensal_ComTicketMedioCalculado()
    {
        var receitas = new[]
        {
            new ReceitaPorPeriodo(2026, 1, 10000m, 20, 50, 500m),
            new ReceitaPorPeriodo(2026, 2, 12000m, 24, 60, 500m)
        };
        _analyticsRepo.GetReceitaPorPeriodoAsync(_empresaId, 12)
            .Returns((IReadOnlyList<ReceitaPorPeriodo>)receitas);

        var result = await _controller.Receita(_empresaId, 12);

        result.Should().BeOfType<OkObjectResult>();
        await _analyticsRepo.Received(1).GetReceitaPorPeriodoAsync(_empresaId, 12);
    }

    // ── Alertas ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Alertas_DeveRetornarOkPaginado()
    {
        var alertas = new[]
        {
            new ValidadeAlerta(Guid.NewGuid(), Guid.NewGuid(), "Produto Vencendo", null, 10, DateTime.UtcNow.AddDays(5), 5, 500m)
        };
        _analyticsRepo.GetAlertasValidadeAsync(_empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ValidadeAlerta>)alertas, 1));

        var result = await _controller.Alertas(_empresaId, null, 30, 1, 20);

        var (items, meta) = OkPaged<ObterAlertasResult>(result);
        meta.Total.Should().Be(1);
        items.Should().ContainSingle();
    }

    // ── Movimentações ──────────────────────────────────────────────────────

    [Fact]
    public async Task Movimentacoes_DeveRetornarOk_FiltrandoPorTipo()
    {
        var de = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ate = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc);
        var resumos = new MovimentacaoResumo[]
        {
            new(2026, 1, 5, TipoMovimentacaoEstoque.Saida, 3, 12, 600m)
        };
        _analyticsRepo
            .GetMovimentacoesResumoAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), TipoMovimentacaoEstoque.Saida)
            .Returns(Task.FromResult<IReadOnlyList<MovimentacaoResumo>>(resumos));

        var result = await _controller.Movimentacoes(_empresaId, de, ate, TipoMovimentacaoEstoque.Saida);

        result.Should().BeOfType<OkObjectResult>();
        await _analyticsRepo.Received(1).GetMovimentacoesResumoAsync(
            _empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), TipoMovimentacaoEstoque.Saida);
    }
}
