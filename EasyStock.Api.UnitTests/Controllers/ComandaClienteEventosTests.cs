using EasyStock.Api.Controllers.Storefront;
using EasyStock.Api.Services.Operacao;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #1719: o stream da comanda do cliente relia o retrato inteiro a cada evento de conversa ou pedido da LOJA.
/// No laboratório, 10 mensagens em outras conversas custaram 578 consultas a uma única comanda aberta.
/// </summary>
public class ComandaClienteEventosTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Conversa = Guid.NewGuid();
    private static readonly Guid Pedido = Guid.NewGuid();
    private readonly OperacaoEventBroker _broker = new(NullLogger<OperacaoEventBroker>.Instance);

    [Fact]
    public void Evento_de_outra_conversa_nao_pede_releitura()
    {
        using var s = _broker.SubscribeOperacao("a", Empresa);
        for (var i = 0; i < 10; i++)
            _broker.PublicarOperacao(Empresa, "conversa.mensagem_recebida", new { conversaId = Guid.NewGuid() });

        ComandaClienteController.EsvaziarEventos(s.Slot, Conversa, Pedido).Should().BeFalse();
        s.Slot.Queue.Should().BeEmpty();
        s.Slot.Signal.CurrentCount.Should().Be(0, "sinal acumulado faria o laço girar uma vez por evento");
    }

    [Fact]
    public void Evento_da_propria_conversa_pede_releitura()
    {
        using var s = _broker.SubscribeOperacao("b", Empresa);
        _broker.PublicarOperacao(Empresa, "conversa.mensagem_recebida", new { conversaId = Guid.NewGuid() });
        _broker.PublicarOperacao(Empresa, "conversa.comanda_atualizada", new { conversaId = Conversa });

        ComandaClienteController.EsvaziarEventos(s.Slot, Conversa, null).Should().BeTrue();
        s.Slot.Signal.CurrentCount.Should().Be(0);
    }

    [Fact]
    public void Evento_do_pedido_da_comanda_pede_releitura()
    {
        using var s = _broker.SubscribeOperacao("c", Empresa);
        _broker.PublicarOperacao(Empresa, "pedido.pago", new { pedidoId = Pedido, valor = 133m });

        ComandaClienteController.EsvaziarEventos(s.Slot, Conversa, Pedido).Should().BeTrue();
    }

    [Fact]
    public void Evento_de_outro_pedido_nao_pede_releitura()
    {
        using var s = _broker.SubscribeOperacao("d", Empresa);
        _broker.PublicarOperacao(Empresa, "pedido.pago", new { pedidoId = Guid.NewGuid(), valor = 133m });

        ComandaClienteController.EsvaziarEventos(s.Slot, Conversa, Pedido).Should().BeFalse();
    }
}
