using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Dashboard;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class FinanceiroControllerTests
{
    private readonly IFluxoCaixaQueries _queries = Substitute.For<IFluxoCaixaQueries>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FinanceiroController _controller;

    private static readonly Guid _empresaId = Guid.NewGuid();

    private static readonly DashboardFinanceiroDto _dashboardVazio = new(0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0);

    public FinanceiroControllerTests()
    {
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _currentUser.TemPermissao(Permissao.VisualizarContasAReceber).Returns(true);

        _controller = new FinanceiroController(
            new ObterDashboardFinanceiroUseCase(_queries),
            new ObterFluxoCaixaUseCase(_queries),
            _currentUser);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static T OkData<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<T>>().Subject;
        return envelope.Data;
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dashboard_DeveRetornarOk_ComKpis()
    {
        var kpis = new DashboardFinanceiroDto(500m, 1200m, 100m, 50m, 800m, 1000m, 3, 5, 1);
        _queries.KpisDashboardAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(kpis);

        var result = await _controller.Dashboard(_empresaId);

        var data = OkData<DashboardFinanceiroDto>(result);
        data.TotalAVencer30dReceber.Should().Be(1200m);
        data.QtdContasReceberAbertas.Should().Be(5);
    }

    [Fact]
    public async Task Dashboard_DeveChamarQueriesComEmpresaId()
    {
        _queries.KpisDashboardAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_dashboardVazio);

        await _controller.Dashboard(_empresaId);

        await _queries.Received(1).KpisDashboardAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dashboard_DeveRetornarBadRequest_QuandoEmpresaIdVazioEhSuperAdmin()
    {
        var result = await _controller.Dashboard(Guid.Empty);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Dashboard_DeveRetornarForbid_QuandoUsuarioSemPermissao()
    {
        _currentUser.TemPermissao(Permissao.VisualizarContasAPagar).Returns(false);
        _currentUser.TemPermissao(Permissao.VisualizarContasAReceber).Returns(false);

        var result = await _controller.Dashboard(_empresaId);

        result.Should().BeOfType<ForbidResult>();
    }

    // ── FluxoCaixa ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FluxoCaixa_DeveRetornarOk_ComBucketsMensais()
    {
        var buckets = new[]
        {
            new FluxoBucketDto(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc),
                "Jan/2026", 500m, 1500m, 450m, 1400m)
        };
        _queries.FluxoBucketsAsync(
            _empresaId,
            PeriodicidadeFluxo.Mensal,
            Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            null, null,
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FluxoBucketDto>>(buckets));

        var result = await _controller.FluxoCaixa(_empresaId);

        var data = OkData<IReadOnlyList<FluxoBucketDto>>(result);
        data.Should().ContainSingle();
        data[0].Rotulo.Should().Be("Jan/2026");
    }

    [Fact]
    public async Task FluxoCaixa_DevePassarPeriodicidadeCorreta()
    {
        _queries.FluxoBucketsAsync(
            _empresaId,
            PeriodicidadeFluxo.Diario,
            Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            null, null,
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FluxoBucketDto>>([]));

        await _controller.FluxoCaixa(_empresaId, periodicidade: PeriodicidadeFluxo.Diario);

        await _queries.Received(1).FluxoBucketsAsync(
            _empresaId,
            PeriodicidadeFluxo.Diario,
            Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            null, null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FluxoCaixa_DeveRetornarForbid_QuandoUsuarioSemPermissao()
    {
        _currentUser.TemPermissao(Permissao.VisualizarContasAPagar).Returns(false);
        _currentUser.TemPermissao(Permissao.VisualizarContasAReceber).Returns(false);

        var result = await _controller.FluxoCaixa(_empresaId);

        result.Should().BeOfType<ForbidResult>();
    }
}
