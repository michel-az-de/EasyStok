using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Services;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

/// <summary>#2003 (estoque-17): a regra única de consumo de lotes da venda, da esteira do pedido e do PWA.</summary>
public class ConsumoDeLotesTests
{
    private static readonly DateTime Agora = new(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);
    private static readonly RegraDeConsumo Padrao = new();

    private static ItemEstoque Lote(string rotulo, decimal saldo, int? diasParaVencer, int diasDesdeAEntrada = 1, int minutosAposAEntrada = 0)
    {
        var entrada = Agora.AddDays(-diasDesdeAEntrada);
        return new ItemEstoque
        {
            Id = Guid.NewGuid(), ProdutoId = Guid.NewGuid(), CodigoInterno = rotulo,
            QuantidadeInicial = Quantidade.From(saldo), QuantidadeAtual = Quantidade.From(saldo), Status = StatusItemEstoque.Ok,
            ValidadeEm = diasParaVencer is { } d ? Validade.From(Agora.Date.AddDays(d)) : null,
            EntradaEm = entrada, CriadoEm = entrada.AddMinutes(minutosAposAEntrada)
        };
    }

    private static string[] Ordem(IEnumerable<ItemEstoque> lotes, RegraDeConsumo regra) =>
        ConsumoDeLotes.Ordenar(lotes, Agora, regra).Select(l => l.CodigoInterno!).ToArray();

    [Fact]
    public void Fefo_vence_antes_sai_antes_e_sem_validade_fica_por_ultimo()
    {
        var lotes = new[] { Lote("SEM_VALIDADE", 1, null, 9), Lote("DEPOIS", 1, 30, 5), Lote("ANTES", 1, 3, 1) };

        Ordem(lotes, Padrao).Should().Equal("ANTES", "DEPOIS", "SEM_VALIDADE");
    }

    [Fact]
    public void Vencido_vai_depois_dos_que_ainda_valem()
    {
        var lotes = new[] { Lote("VENCIDO", 1, -1, 9), Lote("VALE", 1, 20, 1) };

        Ordem(lotes, Padrao).Should().Equal("VALE", "VENCIDO");
    }

    [Fact]
    public void Baixa_de_perda_pede_o_vencido_primeiro()
    {
        var lotes = new[] { Lote("VALE", 1, 20, 1), Lote("VENCIDO", 1, -1, 9) };

        Ordem(lotes, new RegraDeConsumo(VencidoPorUltimo: false)).Should().Equal("VENCIDO", "VALE");
    }

    [Fact]
    public void Validade_igual_desempata_pela_entrada_e_depois_pela_criacao()
    {
        var lotes = new[]
        {
            Lote("NOVO", 1, 10, diasDesdeAEntrada: 1),
            Lote("VELHO_CRIADO_DEPOIS", 1, 10, diasDesdeAEntrada: 6, minutosAposAEntrada: 5),
            Lote("VELHO", 1, 10, diasDesdeAEntrada: 6),
        };

        Ordem(lotes, Padrao).Should().Equal("VELHO", "VELHO_CRIADO_DEPOIS", "NOVO");
    }

    [Fact]
    public void FifoPuro_ignora_a_validade()
    {
        var lotes = new[] { Lote("VENCE_ANTES", 1, 3, diasDesdeAEntrada: 1), Lote("ENTROU_ANTES", 1, 30, diasDesdeAEntrada: 9) };

        Ordem(lotes, new RegraDeConsumo(Fefo: false)).Should().Equal("ENTROU_ANTES", "VENCE_ANTES");
    }

    [Fact]
    public void Planejar_atravessa_os_lotes_e_pula_o_que_esta_zerado()
    {
        var lotes = new[] { Lote("ZERADO", 0, 1), Lote("A", 2, 5), Lote("B", 5, 20) };

        var plano = ConsumoDeLotes.Planejar(lotes, 4, Agora, Padrao);

        plano.Consumos.Select(c => (c.Lote.CodigoInterno, c.Quantidade, c.Descoberto)).Should().Equal(("A", 2m, 0m), ("B", 2m, 0m));
        plano.Falta.Should().Be(0);
    }

    [Fact]
    public void Planejar_a_falta_cai_no_ultimo_lote_que_baixou()
    {
        var lotes = new[] { Lote("A", 1, 5), Lote("B", 2, 20) };

        var plano = ConsumoDeLotes.Planejar(lotes, 5, Agora, Padrao);

        plano.Consumos.Select(c => (c.Lote.CodigoInterno, c.Quantidade, c.Descoberto)).Should().Equal(("A", 1m, 0m), ("B", 4m, 2m));
        plano.Falta.Should().Be(2);
    }

    [Fact]
    public void Planejar_sem_saldo_nenhum_a_falta_cai_no_primeiro_da_ordem()
    {
        var lotes = new[] { Lote("DEPOIS", 0, 30), Lote("ANTES", 0, 3) };

        var plano = ConsumoDeLotes.Planejar(lotes, 2, Agora, Padrao);

        plano.Consumos.Should().ContainSingle().Which.Should().Match<ConsumoDeLote>(c =>
            c.Lote.CodigoInterno == "ANTES" && c.Quantidade == 2 && c.Descoberto == 2);
    }

    [Fact]
    public void Planejar_sem_lote_nenhum_devolve_a_falta_sem_alvo()
    {
        var plano = ConsumoDeLotes.Planejar([], 2, Agora, Padrao);

        plano.Consumos.Should().BeEmpty();
        plano.Falta.Should().Be(2);
    }

    [Fact]
    public void Aplicar_baixa_os_lotes_e_a_falta_vira_descoberto()
    {
        var a = Lote("A", 1, 5);
        var b = Lote("B", 2, 20);

        ConsumoDeLotes.Planejar([a, b], 5, Agora, Padrao).Aplicar(Agora, Agora, permitirVencido: true);

        (a.QuantidadeAtual.Value, a.QuantidadeDescoberta.Value).Should().Be((0m, 0m));
        (b.QuantidadeAtual.Value, b.QuantidadeDescoberta.Value).Should().Be((0m, 2m));
    }

    [Fact]
    public void Aplicar_respeita_o_guard_de_vencido_quando_nao_permitido()
    {
        var vencido = Lote("VENCIDO", 5, -1);

        var baixar = () => ConsumoDeLotes.Planejar([vencido], 2, Agora, Padrao).Aplicar(Agora, Agora, permitirVencido: false);

        baixar.Should().Throw<ItemEstoqueVencidoException>();
        vencido.QuantidadeAtual.Value.Should().Be(5);
    }

    [Fact]
    public void Velocidade_depois_da_saida_e_da_devolucao_usa_a_mesma_janela()
    {
        ItemEstoque.VelocidadeAposSaida(taxaDiariaAnterior: 1m, quantidadeSaida: 15m).Should().Be(1.5m);
        ItemEstoque.VelocidadeAposDevolucao(taxaDiariaAnterior: 1.5m, quantidadeDevolvida: 15m).Should().Be(1m);
        ItemEstoque.VelocidadeAposDevolucao(taxaDiariaAnterior: 0.1m, quantidadeDevolvida: 15m).Should().Be(0m, "nunca abaixo de zero");
    }

    [Fact]
    public void DevolucaoDeSaida_abate_o_descoberto_antes_de_voltar_ao_saldo_e_marca_a_saida()
    {
        var lote = Lote("A", 0, 5);
        lote.QuantidadeDescoberta = Quantidade.From(2);
        var saida = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = Guid.NewGuid(), ProdutoId = lote.ProdutoId, ItemEstoqueId = lote.Id,
            Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = Quantidade.From(5), DocumentoReferencia = "pedido:1",
        };

        var estorno = DevolucaoDeSaida.Aplicar(saida, lote, Agora, "Cancelamento", "motivo");

        (lote.QuantidadeAtual.Value, lote.QuantidadeDescoberta.Value).Should().Be((3m, 0m));
        saida.EstornadaEm.Should().Be(Agora);
        estorno.Should().Match<MovimentacaoEstoque>(e =>
            e.Tipo == TipoMovimentacaoEstoque.Entrada && e.Natureza == NaturezaMovimentacaoEstoque.Estorno
            && e.MovimentacaoEstornadaId == saida.Id && e.DocumentoReferencia == "pedido:1" && e.MotivoEstorno == "motivo");
    }

    [Fact]
    public void DevolucaoDeSaida_sem_lote_so_marca_e_estorna_a_movimentacao()
    {
        var saida = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(), Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = Quantidade.From(1),
        };

        var estorno = DevolucaoDeSaida.Aplicar(saida, null, Agora, null);

        saida.EstornadaEm.Should().NotBeNull();
        estorno.MovimentacaoEstornadaId.Should().Be(saida.Id);
    }
}
