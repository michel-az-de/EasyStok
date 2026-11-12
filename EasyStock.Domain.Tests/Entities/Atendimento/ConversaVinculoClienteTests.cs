using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

public sealed class ConversaVinculoClienteTests
{
    [Fact]
    public void TelefoneAmbiguoExigeEscolhaExplicitaSemClienteAnterior()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "5511999990001", DateTime.UtcNow);
        conversa.ExigirConferenciaCliente();
        conversa.ExigirConferenciaCliente();
        conversa.VinculoRevisao.Should().Be(1);
        conversa.ClienteAnteriorId.Should().BeNull();
        var cliente = Guid.NewGuid();
        conversa.Invoking(c => c.VincularCliente(cliente)).Should().Throw<RegraDeDominioVioladaException>();
        conversa.CorrigirVinculoCliente(cliente, 1);
        conversa.ClienteId.Should().Be(cliente);
        conversa.VinculoEmRevisao.Should().BeFalse();
    }

    [Fact]
    public void CorrigirPreservaContextoEExigeConfirmacaoHumanaParaRestaurar()
    {
        var cliente = Guid.NewGuid();
        var conversa = Conversa.Abrir(Guid.NewGuid(), "5511999990001", DateTime.UtcNow, "Maria", cliente);
        conversa.DefinirContexto("{\"rascunho\":\"preservado\"}");
        conversa.CorrigirVinculoCliente(null, 0);
        conversa.ClienteId.Should().BeNull();
        conversa.ClienteAnteriorId.Should().Be(cliente);
        conversa.VinculoRevisao.Should().Be(1);
        conversa.VinculoEmRevisao.Should().BeTrue();
        var automatico = () => conversa.VincularCliente(cliente);
        automatico.Should().Throw<RegraDeDominioVioladaException>();
        conversa.CorrigirVinculoCliente(cliente, 1);
        conversa.ClienteId.Should().Be(cliente);
        conversa.VinculoRevisao.Should().Be(2);
        conversa.ContextoJson.Should().Be("{\"rascunho\":\"preservado\"}");
    }

    [Fact]
    public void PedidoEncerradoContinuaBloqueandoCorrecao()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "5511999990001", DateTime.UtcNow, clienteId: Guid.NewGuid());
        var pedido = Guid.NewGuid();
        conversa.DefinirPedidoEmAndamento(pedido);
        conversa.DefinirPedidoEmAndamento(null);
        conversa.PedidosHistoricoIds.Should().ContainSingle().Which.Should().Be(pedido);
        var corrigir = () => conversa.CorrigirVinculoCliente(null, 0);
        corrigir.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*já possui pedido*");
    }

    [Fact]
    public void RevisaoAntigaETrocaDiretaSaoRecusadas()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "5511999990001", DateTime.UtcNow, clienteId: Guid.NewGuid());
        var stale = () => conversa.CorrigirVinculoCliente(null, 1);
        stale.Should().Throw<RegraDeDominioVioladaException>();
        var troca = () => conversa.CorrigirVinculoCliente(Guid.NewGuid(), 0);
        troca.Should().Throw<RegraDeDominioVioladaException>();
        conversa.VinculoRevisao.Should().Be(0);
    }
}
