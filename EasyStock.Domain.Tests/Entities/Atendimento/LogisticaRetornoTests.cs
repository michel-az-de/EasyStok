using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

public sealed class LogisticaRetornoTests
{
    [Fact]
    public void Retorno_fisico_libera_viagem_e_preserva_estado_de_estoque_do_pedido()
    {
        var empresa = Guid.NewGuid();
        var pedido = Pedido.Criar(empresa); pedido.Status = "pronto";
        var viagem = Viagem.Criar(empresa, DateTime.UtcNow);
        var entregador = Entregador.Criar(empresa, "Entregador teste", TipoEntregador.Proprio, EmpresaEntregador.Propria, null, null, null, DateTime.UtcNow);
        viagem.DefinirEntregador(entregador); viagem.IncluirParada(pedido.Id, false);
        viagem.Sair(entregador, DateTime.UtcNow); pedido.MudarStatus(StatusPedido.SaiuParaEntrega);
        pedido.RegistrarRetornoEntrega(); viagem.RegistrarRetorno(DateTime.UtcNow);
        pedido.Status.Should().Be("pronto");
        PedidoStateMachine.DescontaEstoque(pedido.StatusEnum).Should().BeTrue();
        viagem.Situacao.Should().Be(SituacaoViagem.Desfeita);
        viagem.Paradas.Should().BeEmpty(); viagem.ConcluidaEm.Should().NotBeNull();
    }

    [Theory]
    [InlineData("pronto")]
    [InlineData("cancelado")]
    [InlineData("entregue")]
    public void Retorno_nao_reabre_pedido_fora_da_entrega(string status)
    {
        var pedido = Pedido.Criar(Guid.NewGuid()); pedido.Status = status;
        FluentActions.Invoking(pedido.RegistrarRetornoEntrega).Should().Throw<RegraDeDominioVioladaException>();
        pedido.Status.Should().Be(status);
    }

    [Fact]
    public void Viagem_que_nao_saiu_nao_tem_retorno_fisico()
    {
        var viagem = Viagem.Criar(Guid.NewGuid(), DateTime.UtcNow);
        FluentActions.Invoking(() => viagem.RegistrarRetorno(DateTime.UtcNow)).Should().Throw<RegraDeDominioVioladaException>();
    }
}
