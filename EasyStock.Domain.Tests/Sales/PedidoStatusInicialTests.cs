using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>
/// #2174: o pedido do storefront nasce no status de entrada do fluxo dele (Rascunho no checkout pago,
/// AguardandoAprovacaoBaba no cardápio guest) pela fábrica, sem atribuir Status de fora do Domain.
/// </summary>
public class PedidoStatusInicialTests
{
    [Fact]
    public void Criar_sem_status_inicial_nasce_aguardando()
    {
        Pedido.Criar(Guid.NewGuid()).StatusEnum.Should().Be(StatusPedido.Aguardando);
    }

    [Theory]
    [InlineData(StatusPedido.Rascunho)]
    [InlineData(StatusPedido.AguardandoAprovacaoBaba)]
    public void Criar_com_status_de_entrada_do_storefront(StatusPedido entrada)
    {
        Pedido.Criar(Guid.NewGuid(), statusInicial: entrada).StatusEnum.Should().Be(entrada);
    }

    [Theory]
    [InlineData(StatusPedido.Preparando)]
    [InlineData(StatusPedido.Pronto)]
    [InlineData(StatusPedido.SaiuParaEntrega)]
    [InlineData(StatusPedido.Entregue)]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.AguardandoPagamento)]
    [InlineData(StatusPedido.AprovadoBaba)]
    public void Criar_recusa_status_que_nao_e_de_entrada(StatusPedido status)
    {
        var criar = () => Pedido.Criar(Guid.NewGuid(), statusInicial: status);

        criar.Should().Throw<RegraDeDominioVioladaException>();
    }
}
