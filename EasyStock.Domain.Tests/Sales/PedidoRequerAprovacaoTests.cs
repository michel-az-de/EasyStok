using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>
/// S12 (#1119): a aprovação manual da dona sai do caminho feliz e fica só para a exceção
/// (ex.: entrega fora de área). O pedido carrega a marca e o motivo.
/// </summary>
public class PedidoRequerAprovacaoTests
{
    [Fact]
    public void Pedido_novo_nao_requer_aprovacao()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());

        pedido.RequerAprovacao.Should().BeFalse();
        pedido.MotivoRequerAprovacao.Should().BeNull();
    }

    [Fact]
    public void MarcarRequerAprovacao_marca_e_guarda_o_motivo()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());

        pedido.MarcarRequerAprovacao("  fora de area  ");

        pedido.RequerAprovacao.Should().BeTrue();
        pedido.MotivoRequerAprovacao.Should().Be("fora de area");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MarcarRequerAprovacao_exige_motivo(string? motivo)
    {
        var pedido = Pedido.Criar(Guid.NewGuid());

        var act = () => pedido.MarcarRequerAprovacao(motivo!);

        act.Should().Throw<ArgumentException>();
        pedido.RequerAprovacao.Should().BeFalse();
    }

    [Fact]
    public void MarcarRequerAprovacao_corta_motivo_longo_no_limite_da_coluna()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());

        pedido.MarcarRequerAprovacao(new string('x', 500));

        pedido.MotivoRequerAprovacao!.Length.Should().Be(Pedido.MotivoRequerAprovacaoMaxLength);
    }

    [Fact]
    public void MudarStatus_de_pronto_para_saiu_para_entrega_grava_a_string_canonica()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.Status = StatusPedidoMapper.Pronto;

        pedido.MudarStatus(StatusPedido.SaiuParaEntrega);

        pedido.Status.Should().Be("saiu_para_entrega");
        pedido.EntreguEm.Should().BeNull();
    }
}
