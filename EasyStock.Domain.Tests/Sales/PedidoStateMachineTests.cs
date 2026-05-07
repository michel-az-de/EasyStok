using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

public class PedidoStateMachineTests
{
    public static IEnumerable<object[]> TransicoesValidas() => new[]
    {
        new object[] { StatusPedido.Aguardando, StatusPedido.Preparando },
        new object[] { StatusPedido.Aguardando, StatusPedido.Cancelado },
        new object[] { StatusPedido.Preparando, StatusPedido.Pronto },
        new object[] { StatusPedido.Preparando, StatusPedido.Cancelado },
        new object[] { StatusPedido.Pronto, StatusPedido.Entregue },
        new object[] { StatusPedido.Pronto, StatusPedido.Cancelado },
        new object[] { StatusPedido.Entregue, StatusPedido.Cancelado },
    };

    public static IEnumerable<object[]> TransicoesInvalidas() => new[]
    {
        // Skip estados (não pode pular preparando)
        new object[] { StatusPedido.Aguardando, StatusPedido.Pronto },
        new object[] { StatusPedido.Aguardando, StatusPedido.Entregue },
        new object[] { StatusPedido.Preparando, StatusPedido.Entregue },

        // Voltar atrás (irreversível)
        new object[] { StatusPedido.Preparando, StatusPedido.Aguardando },
        new object[] { StatusPedido.Pronto, StatusPedido.Preparando },
        new object[] { StatusPedido.Entregue, StatusPedido.Preparando },
        new object[] { StatusPedido.Entregue, StatusPedido.Pronto },

        // Mesmo estado (idempotência é responsabilidade do caller, máquina rejeita)
        new object[] { StatusPedido.Aguardando, StatusPedido.Aguardando },
        new object[] { StatusPedido.Cancelado, StatusPedido.Cancelado },

        // Cancelado é estado final — nada sai
        new object[] { StatusPedido.Cancelado, StatusPedido.Aguardando },
        new object[] { StatusPedido.Cancelado, StatusPedido.Preparando },
        new object[] { StatusPedido.Cancelado, StatusPedido.Pronto },
        new object[] { StatusPedido.Cancelado, StatusPedido.Entregue },
    };

    [Theory]
    [MemberData(nameof(TransicoesValidas))]
    public void PodeTransicionar_retorna_true_em_transicao_valida(StatusPedido de, StatusPedido para)
    {
        PedidoStateMachine.PodeTransicionar(de, para).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(TransicoesInvalidas))]
    public void PodeTransicionar_retorna_false_em_transicao_invalida(StatusPedido de, StatusPedido para)
    {
        PedidoStateMachine.PodeTransicionar(de, para).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(TransicoesValidas))]
    public void EnsureTransicaoValida_nao_lanca_em_transicao_valida(StatusPedido de, StatusPedido para)
    {
        Action act = () => PedidoStateMachine.EnsureTransicaoValida(de, para);
        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(TransicoesInvalidas))]
    public void EnsureTransicaoValida_lanca_TransicaoInvalida_em_invalida(StatusPedido de, StatusPedido para)
    {
        Action act = () => PedidoStateMachine.EnsureTransicaoValida(de, para);
        act.Should().Throw<TransicaoInvalidaException>()
            .Where(ex => ex.De == de && ex.Para == para);
    }

    [Fact]
    public void TransicaoInvalida_mensagem_contem_status_format()
    {
        var ex = Assert.Throws<TransicaoInvalidaException>(() =>
            PedidoStateMachine.EnsureTransicaoValida(StatusPedido.Cancelado, StatusPedido.Aguardando));

        ex.Message.Should().Contain("cancelado").And.Contain("aguardando");
    }

    [Fact]
    public void Transicoes_cobre_todos_os_status_como_origem()
    {
        var todosStatus = Enum.GetValues<StatusPedido>();
        foreach (var status in todosStatus)
        {
            PedidoStateMachine.Transicoes.Should().ContainKey(status,
                $"todo status precisa ter entrada em Transicoes (mesmo que vazia, como Cancelado)");
        }
    }

    [Fact]
    public void Cancelado_nao_tem_destinos()
    {
        PedidoStateMachine.Transicoes[StatusPedido.Cancelado].Should().BeEmpty();
    }

    [Theory]
    [InlineData(StatusPedido.Aguardando, true)]
    [InlineData(StatusPedido.Preparando, true)]
    [InlineData(StatusPedido.Pronto, true)]
    [InlineData(StatusPedido.Entregue, false)]
    [InlineData(StatusPedido.Cancelado, false)]
    public void EstaAberto_classifica_corretamente(StatusPedido status, bool esperado)
    {
        PedidoStateMachine.EstaAberto(status).Should().Be(esperado);
    }

    [Theory]
    [InlineData(StatusPedido.Aguardando, false)]
    [InlineData(StatusPedido.Preparando, false)]
    [InlineData(StatusPedido.Pronto, false)]
    [InlineData(StatusPedido.Entregue, true)]
    [InlineData(StatusPedido.Cancelado, true)]
    public void EstaFinalizado_classifica_corretamente(StatusPedido status, bool esperado)
    {
        PedidoStateMachine.EstaFinalizado(status).Should().Be(esperado);
    }

    [Theory]
    [InlineData(StatusPedido.Aguardando, false)]
    [InlineData(StatusPedido.Preparando, false)]
    [InlineData(StatusPedido.Pronto, true)]
    [InlineData(StatusPedido.Entregue, true)]
    [InlineData(StatusPedido.Cancelado, false)]
    public void DescontaEstoque_classifica_corretamente(StatusPedido status, bool esperado)
    {
        PedidoStateMachine.DescontaEstoque(status).Should().Be(esperado);
    }
}
