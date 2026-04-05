using EasyStock.Application.Ports.Output.Persistence;
using FluentAssertions;
using Xunit;

namespace EasyStock.Application.Tests.Analytics;

/// <summary>
/// Testes de cálculo puro para os DTOs e lógicas analíticas do módulo de analytics.
/// Não dependem de banco ou infraestrutura; verificam apenas as fórmulas.
/// </summary>
public class AnalyticsCalculationTests
{
    // -----------------------------------------------------------------------------
    // Margem
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(100.00, 170.00, 70.00, 70.00)]
    [InlineData(200.00, 280.00, 80.00, 40.00)]
    [InlineData(50.00, 50.00, 0.00, 0.00)]
    public void MargemPorProduto_CalculoDeveEstarCorreto(
        decimal custoMedio,
        decimal precoMedio,
        decimal margemAbs,
        decimal margemPct)
    {
        var margemCalculada = precoMedio - custoMedio;
        var pctCalculada = custoMedio > 0 ? Math.Round(margemCalculada / custoMedio * 100m, 2) : 0m;

        Math.Round(margemCalculada, 2).Should().Be(margemAbs);
        pctCalculada.Should().Be(margemPct);
    }

    [Fact]
    public void MargemPorProduto_CustoZero_NaoDeveGerarDivisaoPorZero()
    {
        decimal custo = 0m;
        decimal preco = 150m;

        var pct = custo > 0 ? (preco - custo) / custo * 100m : 0m;

        pct.Should().Be(0m);
    }

    [Fact]
    public void MargemPorProduto_DeveOrdenarPorMaiorPercentual()
    {
        var margens = new List<MargemPorProduto>
        {
            new(Guid.NewGuid(), "B", 200m, 280m, 80m, 40m, 8),
            new(Guid.NewGuid(), "A", 100m, 170m, 70m, 70m, 15),
            new(Guid.NewGuid(), "C", 300m, 330m, 30m, 10m, 5)
        };

        var ordenados = margens.OrderByDescending(m => m.MargemPercentual).ToList();

        ordenados[0].NomeProduto.Should().Be("A");
        ordenados[1].NomeProduto.Should().Be("B");
        ordenados[2].NomeProduto.Should().Be("C");
    }

    // -----------------------------------------------------------------------------
    // Receita
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(12000.00, 24, 500.00)]
    [InlineData(7500.00, 15, 500.00)]
    [InlineData(0.00, 0, 0.00)]
    public void ReceitaPorPeriodo_TicketMedioDeveEstarCorreto(
        decimal receitaBruta,
        int totalVendas,
        decimal ticketEsperado)
    {
        var ticket = totalVendas > 0
            ? Math.Round(receitaBruta / totalVendas, 2)
            : 0m;

        ticket.Should().Be(ticketEsperado);
    }

    [Fact]
    public void ReceitaPorPeriodo_SomaDeReceitasMensaisDeveIgualarReceitaAnual()
    {
        var meses = new List<ReceitaPorPeriodo>
        {
            new(2024, 1, 10_000m, 20, 50, 500m),
            new(2024, 2, 12_000m, 24, 60, 500m),
            new(2024, 3, 8_000m, 16, 40, 500m),
            new(2024, 4, 11_000m, 22, 55, 500m),
            new(2024, 5, 9_500m, 19, 47, 500m),
            new(2024, 6, 13_000m, 26, 65, 500m),
            new(2024, 7, 14_000m, 28, 70, 500m),
            new(2024, 8, 10_500m, 21, 52, 500m),
            new(2024, 9, 11_500m, 23, 57, 500m),
            new(2024, 10, 12_500m, 25, 62, 500m),
            new(2024, 11, 15_000m, 30, 75, 500m),
            new(2024, 12, 18_000m, 36, 90, 500m)
        };

        var totalAnual = meses.Sum(m => m.ReceitaBruta);
        var totalVendasAnual = meses.Sum(m => m.TotalVendas);

        totalAnual.Should().Be(145_000m);
        totalVendasAnual.Should().Be(290);
    }

    [Fact]
    public void ReceitaPorPeriodo_DeveCrescerAoLongoDoAno()
    {
        var meses = Enumerable.Range(1, 12)
            .Select(m => new ReceitaPorPeriodo(2024, m, m * 1000m, m * 2, m * 5, 500m))
            .ToList();

        meses.Last().ReceitaBruta.Should().BeGreaterThan(meses.First().ReceitaBruta);
    }

    // -----------------------------------------------------------------------------
    // Sazonalidade / Média móvel
    // -----------------------------------------------------------------------------

    [Fact]
    public void SazonalidadeMensal_MediaMovelTresMeses_PrimeiroMesDeveSerIgualAoProprioMes()
    {
        var dados = new[] { 30, 45, 20 };
        var medias = CalcularMediaMovel(dados);

        medias[0].Should().Be(30m);
    }

    [Fact]
    public void SazonalidadeMensal_MediaMovelTresMeses_SegundoMesDeveSerMediaDeDois()
    {
        var dados = new[] { 30, 40, 50 };
        var medias = CalcularMediaMovel(dados);

        medias[1].Should().Be(35m);
    }

    [Fact]
    public void SazonalidadeMensal_MediaMovelTresMeses_TerceiroMesEmDianteDeveUsarTres()
    {
        var dados = new[] { 30, 40, 50, 20, 60 };
        var medias = CalcularMediaMovel(dados);

        medias[2].Should().Be(40m);
        medias[3].Should().Be(Math.Round((40m + 50m + 20m) / 3m, 2));
    }

    [Fact]
    public void SazonalidadeMensal_SerieZerada_DeveRetornarMediasZero()
    {
        var dados = new[] { 0, 0, 0, 0 };
        var medias = CalcularMediaMovel(dados);

        medias.Should().AllSatisfy(m => m.Should().Be(0m));
    }

    // -----------------------------------------------------------------------------
    // Projeção de ruptura
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 2.0, 50)]
    [InlineData(30, 0.5, 60)]
    [InlineData(7, 1.0, 7)]
    public void ProjecaoRuptura_DiasAteRupturaDeveEstarCorreto(int quantidade, double taxa, int diasEsperados)
    {
        var taxaDecimal = (decimal)taxa;
        var diasCalculados = taxaDecimal > 0
            ? (int)Math.Floor(quantidade / taxaDecimal)
            : (int?)null;

        diasCalculados.Should().Be(diasEsperados);
    }

    [Fact]
    public void ProjecaoRuptura_TaxaZero_DiasAteRupturaNulo()
    {
        decimal taxa = 0m;
        decimal estoque = 50m;
        var diasAte = taxa > 0 ? (int?)Math.Floor(estoque / taxa) : null;

        diasAte.Should().BeNull();
    }

    [Fact]
    public void ProjecaoRuptura_DevePriorizar_MenorDiasAteRuptura()
    {
        var projecoes = new List<ProjecaoRuptura>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "B", null, 50, 1m, 50, DateTime.UtcNow.AddDays(50)),
            new(Guid.NewGuid(), Guid.NewGuid(), "A", null, 3, 2m, 1, DateTime.UtcNow.AddDays(1)),
            new(Guid.NewGuid(), Guid.NewGuid(), "C", null, 0, 0m, null, null)
        };

        var ordenados = projecoes
            .OrderBy(p => p.DiasAteRuptura ?? int.MaxValue)
            .ToList();

        ordenados[0].NomeProduto.Should().Be("A");
        ordenados[1].NomeProduto.Should().Be("B");
        ordenados[2].NomeProduto.Should().Be("C");
    }

    // -----------------------------------------------------------------------------
    // Reposição
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 5, 1.4, 30, 40)]
    [InlineData(0, 5, 0.0, 0, 10)]
    public void ReposicaoSugerida_QuantidadeReposicaoDeveCobrirTrintaDias(
        int qtdAtual,
        int qtdMinima,
        double taxa,
        int diasCobertura,
        int qtdRepoEsperada)
    {
        _ = diasCobertura;

        var taxaD = (decimal)taxa;
        var coberturaSugerida = taxaD > 0
            ? (int)Math.Ceiling(taxaD * 30)
            : qtdMinima * 2;
        var qtdRepor = Math.Max(0, coberturaSugerida - qtdAtual);

        qtdRepor.Should().Be(qtdRepoEsperada);
    }

    [Fact]
    public void ReposicaoSugerida_CustoEstimadoDeveSerQtdVezesCusto()
    {
        const int qtdRepor = 37;
        const decimal custo = 50m;
        var custoEstimado = Math.Round(qtdRepor * custo, 2);

        custoEstimado.Should().Be(1850m);
    }

    // -----------------------------------------------------------------------------
    // Validade / Alertas
    // -----------------------------------------------------------------------------

    [Fact]
    public void ValidadeAlerta_DiasAteVencimentoNuncaDeveSerNegativo()
    {
        var hoje = DateTime.UtcNow.Date;
        var validade = hoje.AddDays(-1);
        var diasAte = Math.Max(0, (validade - hoje).Days);

        diasAte.Should().Be(0);
    }

    [Theory]
    [InlineData(10, 50.00, 500.00)]
    [InlineData(5, 199.90, 999.50)]
    [InlineData(0, 100.00, 0.00)]
    public void ValidadeAlerta_ValorEmRiscoDeveSerQtdVezesCusto(
        int quantidade,
        decimal custo,
        decimal esperado)
    {
        var valorRisco = Math.Round(quantidade * custo, 2);
        valorRisco.Should().Be(esperado);
    }

    // -----------------------------------------------------------------------------
    // Vendas por canal
    // -----------------------------------------------------------------------------

    [Fact]
    public void VendaPorCanal_PercentualTotalDeveSer100()
    {
        var receitas = new[] { 25_000m, 15_000m, 10_000m };
        var total = receitas.Sum();

        var percentuais = receitas
            .Select(r => Math.Round(r / total * 100m, 2))
            .ToList();

        percentuais.Sum().Should().BeApproximately(100m, 0.01m);
    }

    [Fact]
    public void VendaPorCanal_TicketMedioDeveSerReceitaDivididaPorVendas()
    {
        const decimal receita = 25_000m;
        const int vendas = 50;
        var ticket = Math.Round(receita / vendas, 2);

        ticket.Should().Be(500m);
    }

    [Fact]
    public void VendaPorCanal_TotalReceita_Zero_NaoGeraPercentualNaN()
    {
        decimal totalReceita = 0m;
        decimal receitaCanal = 0m;
        var pct = totalReceita > 0 ? receitaCanal / totalReceita * 100m : 0m;

        pct.Should().Be(0m);
    }

    // -----------------------------------------------------------------------------
    // Dashboard
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 30, 3.33)]
    [InlineData(0, 30, 0.00)]
    [InlineData(30, 1, 30.00)]
    public void Dashboard_MediaVendasDiariaDeveEstarCorreta(int totalSaidas, int dias, decimal mediaEsperada)
    {
        var diasEfetivos = Math.Max(1, dias);
        var media = Math.Round((decimal)totalSaidas / diasEfetivos, 2);

        media.Should().Be(mediaEsperada);
    }

    [Fact]
    public void Dashboard_ProjecaoVendasDeveMultiplicarMediaPeloPeriodo()
    {
        const decimal mediaDiaria = 3.5m;
        const int periodo = 30;
        var projecao = Math.Round(mediaDiaria * periodo, 0);

        projecao.Should().Be(105m);
    }

    // -----------------------------------------------------------------------------
    // Helpers privados
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Réplica da lógica de média móvel de 3 meses do AnalyticsRepository.
    /// </summary>
    private static decimal[] CalcularMediaMovel(int[] dados)
    {
        return dados.Select((_, idx) =>
        {
            var janela = dados.Skip(Math.Max(0, idx - 2)).Take(Math.Min(3, idx + 1));
            return janela.Any()
                ? Math.Round((decimal)janela.Average(), 2)
                : 0m;
        }).ToArray();
    }
}
