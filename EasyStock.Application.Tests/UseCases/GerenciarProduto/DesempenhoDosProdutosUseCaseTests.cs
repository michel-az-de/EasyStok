using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.GerenciarProduto;

/// <summary>#3299: o desempenho dos produtos monta janelas no dia de Brasília e a faixa é a soma das linhas.</summary>
public sealed class DesempenhoDosProdutosUseCaseTests
{
    // 24/11/2026 23:30 em Brasília = 25/11 02:30 UTC: o dia operacional ainda é 24.
    private static readonly DateTime Agora = new(2026, 11, 25, 2, 30, 0, DateTimeKind.Utc);
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Lasanha = Guid.NewGuid(), Linguini = Guid.NewGuid(), Molho = Guid.NewGuid(), Novo = Guid.NewGuid(), Insumo = Guid.NewGuid();

    private static DateTime DiasAtras(int dias) => Agora.AddDays(-dias);

    private static (DesempenhoDosProdutosUseCase UseCase, IProdutosDesempenhoQuery Consulta) Montar()
    {
        var consulta = Substitute.For<IProdutosDesempenhoQuery>();
        consulta.CatalogoAsync(Empresa, Arg.Any<CancellationToken>()).Returns(new List<ProdutoDoCatalogo>
        {
            new(Lasanha, "Lasanha", true, DiasAtras(300), true, 45m, 15m),
            new(Linguini, "Linguini", true, DiasAtras(300), true, 19m, null),
            new(Molho, "Molho", false, DiasAtras(300), true, 15m, null),
            new(Novo, "Novo", true, DiasAtras(40), true, null, null),
            new(Insumo, "Farinha", true, DiasAtras(300), false, null, null),
        });
        var vendas = new List<VendaDoProdutoNoPedido>();
        // Lasanha: 4 pedidos nos últimos 90 dias (8 un) e 3 nos 90 anteriores (3 un), vendida a R$ 40 (tabela R$ 45).
        for (var i = 0; i < 4; i++) vendas.Add(new(Lasanha, Guid.NewGuid(), DiasAtras(10 + i * 15), 80m, 2m));
        for (var i = 0; i < 3; i++) vendas.Add(new(Lasanha, Guid.NewGuid(), DiasAtras(100 + i * 20), 40m, 1m));
        // Linguini: vendeu 3 vezes há mais de 90 dias.
        for (var i = 0; i < 3; i++) vendas.Add(new(Linguini, Guid.NewGuid(), DiasAtras(110 + i * 5), 19m, 1m));
        // Molho inativo: 1 venda recente, em pedido próprio.
        vendas.Add(new(Molho, Guid.NewGuid(), DiasAtras(5), 15m, 1m));
        consulta.VendasDesdeAsync(Empresa, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(vendas);
        consulta.HistoricoAsync(Empresa, Arg.Any<CancellationToken>()).Returns(new List<HistoricoDoProduto>
        {
            new(Lasanha, 7, DiasAtras(140), DiasAtras(10)),
            new(Linguini, 3, DiasAtras(120), DiasAtras(110)),
            new(Molho, 1, DiasAtras(5), DiasAtras(5)),
        });
        consulta.ReceitaSemProdutoDesdeAsync(Empresa, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(12m);
        return (new DesempenhoDosProdutosUseCase(consulta, new FakeTimeProvider(new DateTimeOffset(Agora))), consulta);
    }

    [Fact]
    public async Task Janelas_comecam_na_meia_noite_de_Brasilia()
    {
        var (useCase, consulta) = Montar();
        var r = await useCase.ExecuteAsync(Empresa);

        r.Hoje.Should().Be(new DateOnly(2026, 11, 24));
        // 365 dias contando hoje: desde 25/11/2025 00:00 em Brasília (03:00 UTC).
        await consulta.Received(1).VendasDesdeAsync(Empresa, new DateTime(2025, 11, 25, 3, 0, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
        await consulta.Received(1).ReceitaSemProdutoDesdeAsync(Empresa, new DateTime(2026, 8, 27, 3, 0, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Linha_traz_vendas_tendencia_ritmo_curva_e_margem()
    {
        var (useCase, _) = Montar();
        var r = await useCase.ExecuteAsync(Empresa);
        var lasanha = r.Produtos.Single(p => p.ProdutoId == Lasanha);

        lasanha.Receita.Should().Be(320m);
        lasanha.Unidades.Should().Be(8m);
        lasanha.Pedidos.Should().Be(4);
        lasanha.ReceitaAnterior.Should().Be(120m);
        lasanha.Tendencia.Should().Be(DesempenhoDoProduto.Subindo);
        lasanha.RitmoDe30Dias.Should().Be(2.7m);
        lasanha.Curva.Should().Be(DesempenhoDoProduto.CurvaA);
        lasanha.PrecoVendido.Should().Be(40m, "R$ 440 em 11 unidades no ano");
        lasanha.Margem.Should().Be(62.5m, "(40 - 15) / 40 sobre o preço vendido, não o de tabela");
        lasanha.AbaixoDaTabela.Should().BeTrue();
        lasanha.UltimaVenda.Should().Be(new DateOnly(2026, 11, 14));
        lasanha.DiasSemVender.Should().Be(10);
        lasanha.Parado.Should().BeFalse();
    }

    [Fact]
    public async Task Parado_e_nunca_vendeu_so_em_produto_ativo_de_venda()
    {
        var (useCase, _) = Montar();
        var r = await useCase.ExecuteAsync(Empresa);

        r.Produtos.Single(p => p.ProdutoId == Linguini).Parado.Should().BeTrue("3 pedidos em 10 dias e 110 sem vender");
        r.Produtos.Single(p => p.ProdutoId == Novo).NuncaVendeu.Should().BeTrue();
        r.Produtos.Single(p => p.ProdutoId == Insumo).NuncaVendeu.Should().BeFalse("insumo não se vende");
        r.Produtos.Single(p => p.ProdutoId == Molho).Margem.Should().BeNull("sem custo");
    }

    [Fact]
    public async Task Faixa_e_a_soma_das_linhas()
    {
        var (useCase, _) = Montar();
        var r = await useCase.ExecuteAsync(Empresa);
        var f = r.Resumo;

        f.Receita.Should().Be(r.Produtos.Sum(p => p.Receita)).And.Be(335m);
        f.ReceitaAnterior.Should().Be(r.Produtos.Sum(p => p.ReceitaAnterior)).And.Be(177m);
        f.Compras.Should().Be(5, "pedidos distintos com produto na janela");
        f.ProdutosVendidos.Should().Be(2);
        f.ReceitaSemProduto.Should().Be(12m);
        f.Ativos.Should().Be(3, "insumo e inativo fora");
        f.Parados.Should().Be(1);
        f.NuncaVenderam.Should().Be(1);
        f.AbaixoDaTabela.Should().Be(1);
        f.SemCusto.Should().Be(2, "Linguini e Novo");
        f.SemPreco.Should().Be(1, "Novo");
        f.Curva.ProdutosA.Should().Be(1);
        f.Curva.Previa.Should().BeTrue("bem menos de 100 compras no ano");
        f.MaisVendidos.Select(m => m.Nome).Should().Equal("Lasanha", "Molho");
    }
}
