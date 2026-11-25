using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>#3299: as regras do desempenho do produto, honestas com pouco volume.</summary>
public class DesempenhoDoProdutoTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 24);

    [Fact]
    public void Curva_abc_acumula_a_receita_ate_80_e_95_por_cento()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid(), zero = Guid.NewGuid();
        var curva = DesempenhoDoProduto.CurvaAbc([(c, 60m), (a, 700m), (b, 200m), (d, 40m), (zero, 0m)]);

        curva[a].Should().Be(DesempenhoDoProduto.CurvaA, "70% acumulado");
        curva[b].Should().Be(DesempenhoDoProduto.CurvaB, "a linha que cruza 80% fica em B");
        curva[c].Should().Be(DesempenhoDoProduto.CurvaC, "96% acumulado passa de 95%");
        curva[d].Should().Be(DesempenhoDoProduto.CurvaC);
        curva.Should().NotContainKey(zero, "sem venda não entra na curva");
    }

    [Fact]
    public void Curva_abc_poe_o_primeiro_em_A_mesmo_quando_ele_sozinho_passa_de_80()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var curva = DesempenhoDoProduto.CurvaAbc([(a, 900m), (b, 100m)]);
        curva[a].Should().Be(DesempenhoDoProduto.CurvaA);
        curva[b].Should().Be(DesempenhoDoProduto.CurvaC);
    }

    [Fact]
    public void Curva_abc_vazia_sem_receita() =>
        DesempenhoDoProduto.CurvaAbc([]).Should().BeEmpty();

    [Theory]
    [InlineData(3, 10, 3, 6, "subindo")]
    [InlineData(3, 6, 3, 10, "caindo")]
    [InlineData(4, 10, 4, 9, "estavel")]   // 11%: dentro da faixa
    [InlineData(3, 5, 3, 4, "estavel")]    // 25%, mas só 1 unidade
    [InlineData(2, 10, 3, 4, null)]        // base pequena na janela atual
    [InlineData(5, 10, 2, 4, null)]        // base pequena na anterior
    public void Tendencia_exige_base_nas_duas_janelas(int pedidos, int unidades, int pedidosAntes, int unidadesAntes, string? esperado) =>
        DesempenhoDoProduto.Tendencia(pedidos, unidades, pedidosAntes, unidadesAntes).Should().Be(esperado);

    [Theory]
    [InlineData(3, 9, 3.0)]
    [InlineData(5, 10, 3.3)]
    [InlineData(2, 30, null)]
    public void Ritmo_de_30_dias_e_um_terco_dos_90_com_base(int pedidos, int unidades, double? esperado) =>
        DesempenhoDoProduto.RitmoDe30Dias(pedidos, unidades).Should().Be(esperado is { } e ? (decimal)e : null);

    [Fact]
    public void Parado_exige_historico_e_silencio_maior_que_o_ritmo_proprio()
    {
        // 4 pedidos em 30 dias (intervalo 10), sem vender há 90: parado.
        DesempenhoDoProduto.Parado(4, Hoje.AddDays(-120), Hoje.AddDays(-90), Hoje).Should().BeTrue();
        // Só 2 pedidos: venda esporádica, sem alerta.
        DesempenhoDoProduto.Parado(2, Hoje.AddDays(-200), Hoje.AddDays(-100), Hoje).Should().BeFalse();
        // 60 dias exatos não passam de 60.
        DesempenhoDoProduto.Parado(4, Hoje.AddDays(-90), Hoje.AddDays(-60), Hoje).Should().BeFalse();
        // 3 pedidos em 300 dias (intervalo 150): 100 dias é o ritmo dele, não parada.
        DesempenhoDoProduto.Parado(3, Hoje.AddDays(-400), Hoje.AddDays(-100), Hoje).Should().BeFalse();
        DesempenhoDoProduto.Parado(0, null, null, Hoje).Should().BeFalse();
    }

    [Fact]
    public void Nunca_vendeu_so_conta_produto_de_venda_ativo_e_com_30_dias()
    {
        DesempenhoDoProduto.NuncaVendeu(ativo: true, Hoje.AddDays(-30), temVenda: false, deVenda: true, Hoje).Should().BeTrue();
        DesempenhoDoProduto.NuncaVendeu(ativo: true, Hoje.AddDays(-29), temVenda: false, deVenda: true, Hoje).Should().BeFalse();
        DesempenhoDoProduto.NuncaVendeu(ativo: false, Hoje.AddDays(-90), temVenda: false, deVenda: true, Hoje).Should().BeFalse();
        DesempenhoDoProduto.NuncaVendeu(ativo: true, Hoje.AddDays(-90), temVenda: true, deVenda: true, Hoje).Should().BeFalse();
        DesempenhoDoProduto.NuncaVendeu(ativo: true, Hoje.AddDays(-90), temVenda: false, deVenda: false, Hoje).Should().BeFalse("insumo e embalagem não se vendem");
    }

    [Theory]
    [InlineData(450.0, 10.0, 45.0)]       // vendido
    [InlineData(0.0, 0.0, null)]      // sem venda
    [InlineData(100.0, 0.0, null)]      // sem unidade não divide
    public void Preco_vendido_e_a_media_das_vendas(double receita, double unidades, double? esperado) =>
        DesempenhoDoProduto.PrecoVendido((decimal)receita, (decimal)unidades).Should().Be(esperado is { } e ? (decimal)e : null);

    [Theory]
    [InlineData(40.0, 10.0, 75.0)]
    [InlineData(40.0, 50.0, -25.0)]
    [InlineData(30.0, 10.0, 66.7)]
    [InlineData(40.0, null, null)]
    [InlineData(40.0, 0.0, null)]
    [InlineData(0.0, 10.0, null)]
    public void Margem_sobre_o_preco_com_uma_casa(double preco, double? custo, double? esperado) =>
        DesempenhoDoProduto.Margem((decimal)preco, custo is { } c ? (decimal)c : null).Should().Be(esperado is { } e ? (decimal)e : null);

    [Theory]
    [InlineData(43.0, 45.0, true)]
    [InlineData(44.2, 45.0, false)]   // 98,2%: arredondamento e troco
    [InlineData(45.0, null, false)]
    public void Abaixo_da_tabela_com_folga_de_2_por_cento(double vendido, double? tabela, bool esperado) =>
        DesempenhoDoProduto.AbaixoDaTabela((decimal)vendido, tabela is { } t ? (decimal)t : null).Should().Be(esperado);
}
