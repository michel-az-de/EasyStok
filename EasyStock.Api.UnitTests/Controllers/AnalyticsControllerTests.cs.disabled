using EasyStock.Api.Configuration;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class AnalyticsControllerTests
{
    private readonly IAnalyticsRepository _repo = Substitute.For<IAnalyticsRepository>();
    private readonly IConfiguracaoLojaRepository _configuracaoLojaRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AnalyticsController _controller;

    public AnalyticsControllerTests()
    {
        var config = Options.Create(new EasyStockConfiguracoes
        {
            DiasAlertaVencimento = 30,
            DiasItemParado = 90
        });
        _controller = new AnalyticsController(_repo, _configuracaoLojaRepository, config, _currentUser);
    }

    // helpers

    private static ApiResponse<T> OkData<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        return ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<T>>().Subject;
    }

    private static (IEnumerable<T> Items, PagedMeta Meta) OkPaged<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<IEnumerable<T>>>().Subject;
        var meta = envelope.Meta.Should().BeOfType<PagedMeta>().Subject;
        return (envelope.Data, meta);
    }

    // Dashboard

    [Fact]
    public async Task Dashboard_DeveRetornarOk_ComResumoPreenchido()
    {
        var empresaId = Guid.NewGuid();
        var resumo = new DashboardResumo(
            empresaId, 30, 10, 100, 5000m, 3500m, 3.5m, 105m, 8000m, 2, 1, 3);

        _repo.GetDashboardResumoAsync(empresaId, 30).Returns(resumo);

        var result = await _controller.Dashboard(empresaId, 30);

        OkData<DashboardResumo>(result).Data.Should().BeEquivalentTo(resumo);
    }

    [Fact]
    public async Task Dashboard_DeveUsarPeriodoPadrao30_QuandoNaoInformado()
    {
        var empresaId = Guid.NewGuid();
        var resumo = new DashboardResumo(empresaId, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        _repo.GetDashboardResumoAsync(empresaId, 30).Returns(resumo);

        await _controller.Dashboard(empresaId);

        await _repo.Received(1).GetDashboardResumoAsync(empresaId, 30);
    }

    // Projeções

    [Fact]
    public async Task Projecoes_DeveRetornarOk_ComItensPaginados()
    {
        var empresaId = Guid.NewGuid();
        var projecoes = new List<ProjecaoRuptura>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto A", "INT-01", 5, 1.2m, 4, DateTime.UtcNow.AddDays(4)),
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto B", "INT-02", 0, 0m, null, null)
        };
        _repo.GetProjecaoRupturaAsync(empresaId, 30, 1, 20).Returns(((IReadOnlyList<ProjecaoRuptura>)projecoes, 2));

        var (items, meta) = OkPaged<ProjecaoRuptura>(await _controller.Projecoes(empresaId, 30, 1, 20));
        items.Should().HaveCount(2);
        meta.Total.Should().Be(2);
    }

    [Fact]
    public async Task Projecoes_PrimeiroItemDeveSerMaisCritico()
    {
        var empresaId = Guid.NewGuid();
        var critico = new ProjecaoRuptura(Guid.NewGuid(), Guid.NewGuid(), "X", null, 3, 2m, 1, DateTime.UtcNow.AddDays(1));
        var estavel = new ProjecaoRuptura(Guid.NewGuid(), Guid.NewGuid(), "Y", null, 50, 1m, 50, DateTime.UtcNow.AddDays(50));
        _repo.GetProjecaoRupturaAsync(empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ProjecaoRuptura>)new[] { critico, estavel }, 2));

        var (items, _) = OkPaged<ProjecaoRuptura>(await _controller.Projecoes(empresaId));
        items.First().DiasAteRuptura.Should().Be(1);
    }

    // Reposição

    [Fact]
    public async Task Reposicao_DeveRetornarOk_ComSugestoes()
    {
        var empresaId = Guid.NewGuid();
        var sugestoes = new List<ReposicaoSugerida>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto C", "INT-03", 2, 5, 43, 1.4m, 1, 2150m)
        };
        _repo.GetSugestaoReposicaoDetalhadaAsync(empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ReposicaoSugerida>)sugestoes, 1));

        var (_, meta) = OkPaged<ReposicaoSugerida>(await _controller.Reposicao(empresaId, 30, 1, 20));
        meta.Total.Should().Be(1);
    }

    [Fact]
    public async Task Reposicao_CustoEstimadoDeveSerQuantidadeVezesCusto()
    {
        var empresaId = Guid.NewGuid();
        var item = new ReposicaoSugerida(Guid.NewGuid(), Guid.NewGuid(), "P", null, 3, 10, 37, 1.23m, 2, 37 * 50m);
        _repo.GetSugestaoReposicaoDetalhadaAsync(empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ReposicaoSugerida>)new[] { item }, 1));

        var (items, _) = OkPaged<ReposicaoSugerida>(await _controller.Reposicao(empresaId));
        items.First().CustoEstimadoReposicao.Should().Be(1850m);
    }

    // Sazonalidade

    [Fact]
    public async Task Sazonalidade_DeveRetornarListaMensal()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var dados = new List<SazonalidadeMensal>
        {
            new(2024, 1, 30, 1500m, 30m),
            new(2024, 2, 45, 2250m, 37.5m),
            new(2024, 3, 20, 1000m, 31.67m)
        };
        _repo.GetSazonalidadeAsync(empresaId, produtoId, 12)
            .Returns((IReadOnlyList<SazonalidadeMensal>)dados);

        var envelope = OkData<IReadOnlyList<SazonalidadeMensal>>(await _controller.Sazonalidade(empresaId, produtoId, 12));
        envelope.Data.Should().HaveCount(3);
    }

    [Fact]
    public async Task Sazonalidade_MediaMovelTresMesesDeveSerCalculada()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var dados = new List<SazonalidadeMensal>
        {
            new(2024, 1, 30, 0m, 30m),
            new(2024, 2, 40, 0m, 35m),
            new(2024, 3, 50, 0m, 40m)
        };
        _repo.GetSazonalidadeAsync(empresaId, produtoId, 12).Returns((IReadOnlyList<SazonalidadeMensal>)dados);

        var envelope = OkData<IReadOnlyList<SazonalidadeMensal>>(await _controller.Sazonalidade(empresaId, produtoId));
        envelope.Data[1].MediaMovelTresMeses.Should().Be(35m);
        envelope.Data[2].MediaMovelTresMeses.Should().Be(40m);
    }

    // Alertas

    [Fact]
    public async Task Alertas_DeveRetornarOk_ComItensPorVencer()
    {
        var empresaId = Guid.NewGuid();
        var alertas = new List<ValidadeAlerta>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto D", "INT-04", 10, DateTime.UtcNow.AddDays(5), 5, 500m)
        };
        _repo.GetAlertasValidadeAsync(empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ValidadeAlerta>)alertas, 1));

        var (_, meta) = OkPaged<ValidadeAlerta>(await _controller.Alertas(empresaId, null, 30, 1, 20));
        meta.Total.Should().Be(1);
    }

    [Fact]
    public async Task Alertas_ValorEmRiscoDeveSerQuantidadeVezesCusto()
    {
        var empresaId = Guid.NewGuid();
        var alerta = new ValidadeAlerta(Guid.NewGuid(), Guid.NewGuid(), "P", null, 10, DateTime.UtcNow.AddDays(7), 7, 500m);
        _repo.GetAlertasValidadeAsync(empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ValidadeAlerta>)new[] { alerta }, 1));

        var (items, _) = OkPaged<ValidadeAlerta>(await _controller.Alertas(empresaId, null));
        items.First().ValorEmRisco.Should().Be(500m);
    }

    // Receita

    [Fact]
    public async Task Receita_DeveRetornarListaMensal()
    {
        var empresaId = Guid.NewGuid();
        var receitas = new List<ReceitaPorPeriodo>
        {
            new(2024, 1, 10000m, 20, 50, 500m),
            new(2024, 2, 12000m, 24, 60, 500m)
        };
        _repo.GetReceitaPorPeriodoAsync(empresaId, 12).Returns((IReadOnlyList<ReceitaPorPeriodo>)receitas);

        var envelope = OkData<IReadOnlyList<ReceitaPorPeriodo>>(await _controller.Receita(empresaId, 12));
        envelope.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task Receita_TicketMedioDeveSerReceitaDivididaPorVendas()
    {
        var empresaId = Guid.NewGuid();
        var mes = new ReceitaPorPeriodo(2024, 2, 12000m, 24, 60, 500m);
        _repo.GetReceitaPorPeriodoAsync(empresaId, 12).Returns((IReadOnlyList<ReceitaPorPeriodo>)new[] { mes });

        var envelope = OkData<IReadOnlyList<ReceitaPorPeriodo>>(await _controller.Receita(empresaId));
        envelope.Data[0].TicketMedio.Should().Be(500m);
    }

    // Margem

    [Fact]
    public async Task Margem_DeveRetornarOk_ComListaPaginada()
    {
        var empresaId = Guid.NewGuid();
        var margens = new List<MargemPorProduto>
        {
            new(Guid.NewGuid(), "Produto E", 100m, 170m, 70m, 70m, 15),
            new(Guid.NewGuid(), "Produto F", 200m, 280m, 80m, 40m, 8)
        };
        _repo.GetMargemPorProdutoAsync(empresaId, 30, 1, 20)
            .Returns((IReadOnlyList<MargemPorProduto>)margens);

        var (items, _) = OkPaged<MargemPorProduto>(await _controller.Margem(empresaId, 30, 1, 20));
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Margem_MargemPercentualDeveSerAbsolutaDivididaCusto()
    {
        var empresaId = Guid.NewGuid();
        var item = new MargemPorProduto(Guid.NewGuid(), "P", 100m, 170m, 70m, 70m, 10);
        _repo.GetMargemPorProdutoAsync(empresaId, 30, 1, 20)
            .Returns((IReadOnlyList<MargemPorProduto>)new[] { item });

        var (items, _) = OkPaged<MargemPorProduto>(await _controller.Margem(empresaId));
        items.First().MargemPercentual.Should().Be(70m);
    }

    // Movimentações

    [Fact]
    public async Task Movimentacoes_DeveRetornarOk_FiltrandoPorTipo()
    {
        var empresaId = Guid.NewGuid();
        var de = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ate = new DateTime(2024, 1, 31, 23, 59, 59, DateTimeKind.Utc);
        var resumos = new List<MovimentacaoResumo>
        {
            new(2024, 1, 5,  TipoMovimentacaoEstoque.Saida, 3, 12, 600m),
            new(2024, 1, 10, TipoMovimentacaoEstoque.Saida, 1, 5,  250m)
        };
        _repo.GetMovimentacoesResumoAsync(empresaId, de, ate, TipoMovimentacaoEstoque.Saida)
            .Returns((IReadOnlyList<MovimentacaoResumo>)resumos);

        var envelope = OkData<IReadOnlyList<MovimentacaoResumo>>(
            await _controller.Movimentacoes(empresaId, de, ate, TipoMovimentacaoEstoque.Saida));
        envelope.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task Movimentacoes_DeveUsarDatasPadraoQuandoNaoInformadas()
    {
        var empresaId = Guid.NewGuid();
        _repo.GetMovimentacoesResumoAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), null)
            .Returns((IReadOnlyList<MovimentacaoResumo>)new List<MovimentacaoResumo>());

        var result = await _controller.Movimentacoes(empresaId, null, null, null);

        result.Should().BeOfType<OkObjectResult>();
        await _repo.Received(1).GetMovimentacoesResumoAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), null);
    }

    // Validade

    [Fact]
    public async Task Validade_DeveRetornarOk_ComItensProximosAoVencimento()
    {
        var empresaId = Guid.NewGuid();
        var alertas = new List<ValidadeAlerta>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Iogurte", null, 50, DateTime.UtcNow.AddDays(10), 10, 250m)
        };
        _repo.GetAlertasValidadeAsync(empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ValidadeAlerta>)alertas, 1));

        var (items, _) = OkPaged<ValidadeAlerta>(await _controller.Validade(empresaId, null, 30, 1, 20));
        items.Should().ContainSingle();
    }

    [Fact]
    public async Task Validade_DiasAteVencimentoNaoDeveSerNegativo()
    {
        var empresaId = Guid.NewGuid();
        var alerta = new ValidadeAlerta(Guid.NewGuid(), Guid.NewGuid(), "P", null, 5,
            DateTime.UtcNow.AddDays(3), 3, 100m);
        _repo.GetAlertasValidadeAsync(empresaId, 30, 1, 20)
            .Returns(((IReadOnlyList<ValidadeAlerta>)new[] { alerta }, 1));

        var (items, _) = OkPaged<ValidadeAlerta>(await _controller.Validade(empresaId, null));
        items.First().DiasAteVencimento.Should().BeGreaterThanOrEqualTo(0);
    }

    // Parados

    [Fact]
    public async Task Parados_DeveRetornarOk_ComItensImoveis()
    {
        var empresaId = Guid.NewGuid();
        var parados = new List<ItemParadoDetalhe>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto G", "INT-07", 20,
                DateTime.UtcNow.AddDays(-120), 120, 2000m)
        };
        _repo.GetItensParadosDetalhadosAsync(empresaId, 90, 1, 20)
            .Returns(((IReadOnlyList<ItemParadoDetalhe>)parados, 1));

        var (_, meta) = OkPaged<ItemParadoDetalhe>(await _controller.Parados(empresaId, null, 90, 1, 20));
        meta.Total.Should().Be(1);
    }

    [Fact]
    public async Task Parados_ValorParadoDeveSerQuantidadeVezesCusto()
    {
        var empresaId = Guid.NewGuid();
        var item = new ItemParadoDetalhe(Guid.NewGuid(), Guid.NewGuid(), "P", null, 20,
            DateTime.UtcNow.AddDays(-100), 100, 2000m);
        _repo.GetItensParadosDetalhadosAsync(empresaId, 90, 1, 20)
            .Returns(((IReadOnlyList<ItemParadoDetalhe>)new[] { item }, 1));

        var (items, _) = OkPaged<ItemParadoDetalhe>(await _controller.Parados(empresaId, null));
        items.First().ValorParado.Should().Be(2000m);
    }

    // Vendas por canal

    [Fact]
    public async Task VendasPorCanal_DeveRetornarOk_ComCanaisOrdenados()
    {
        var empresaId = Guid.NewGuid();
        var canais = new List<VendaPorCanal>
        {
            new(CanalVenda.MercadoLivre, 50, 200, 25000m, 500m, 62.5m),
            new(CanalVenda.LojaPropria,  30, 100, 15000m, 500m, 37.5m)
        };
        _repo.GetVendasPorCanalAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns((IReadOnlyList<VendaPorCanal>)canais);

        var envelope = OkData<IReadOnlyList<VendaPorCanal>>(await _controller.VendasPorCanal(empresaId, null, null));
        envelope.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task VendasPorCanal_PercentualDeveChegar100_QuandoSomadoTodosCanais()
    {
        var empresaId = Guid.NewGuid();
        var canais = new List<VendaPorCanal>
        {
            new(CanalVenda.MercadoLivre, 50, 200, 25000m, 500m, 62.5m),
            new(CanalVenda.LojaPropria,  30, 100, 15000m, 500m, 37.5m)
        };
        _repo.GetVendasPorCanalAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns((IReadOnlyList<VendaPorCanal>)canais);

        var envelope = OkData<IReadOnlyList<VendaPorCanal>>(await _controller.VendasPorCanal(empresaId, null, null));
        envelope.Data.Sum(c => c.PercentualReceita).Should().BeApproximately(100m, 0.01m);
    }

    [Fact]
    public async Task VendasPorCanal_DeveUsarDatasPadraoQuandoNaoInformadas()
    {
        var empresaId = Guid.NewGuid();
        _repo.GetVendasPorCanalAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns((IReadOnlyList<VendaPorCanal>)new List<VendaPorCanal>());

        await _controller.VendasPorCanal(empresaId, null, null);

        await _repo.Received(1).GetVendasPorCanalAsync(
            empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }
}
