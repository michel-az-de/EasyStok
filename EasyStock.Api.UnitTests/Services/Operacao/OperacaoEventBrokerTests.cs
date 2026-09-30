using EasyStock.Api.Services.Operacao;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Services.Operacao;

/// <summary>
/// S18 (#1146): o broker in-memory entrega evento de operação só para os ouvintes do console da mesma
/// empresa, com o nome do evento no frame SSE. O canal mobile continua recebendo só os seus eventos.
/// </summary>
public class OperacaoEventBrokerTests
{
    private readonly OperacaoEventBroker _broker = new(NullLogger<OperacaoEventBroker>.Instance);

    [Fact]
    public void FiltraPorEmpresa()
    {
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        using var ouvinteA = _broker.SubscribeOperacao("a", empresaA);
        using var ouvinteB = _broker.SubscribeOperacao("b", empresaB);
        var pedidoId = Guid.NewGuid();

        _broker.PublicarOperacao(empresaA, "pedido.pago", new { pedidoId });

        ouvinteA.Slot.Queue.Should().ContainSingle().Which.ParaFrame().Should()
            .Be($"event: pedido.pago\ndata: {{\"pedidoId\":\"{pedidoId}\"}}\n\n");
        ouvinteB.Slot.Queue.Should().BeEmpty("evento da empresa A não pode vazar para a empresa B");
    }

    [Fact]
    public void EventoDeOperacaoNaoChegaAoDeviceMobileDaMesmaEmpresa()
    {
        var empresa = Guid.NewGuid();
        using var device = _broker.Subscribe("device", empresa, Guid.NewGuid(), "dev-1");

        _broker.PublicarOperacao(empresa, "pedido.pago", new { pedidoId = Guid.NewGuid() });

        device.Slot.Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task EventoMobileNaoChegaAoConsoleESegueSemNome()
    {
        var empresa = Guid.NewGuid();
        var loja = Guid.NewGuid();
        using var console = _broker.SubscribeOperacao("console", empresa);
        using var outroDevice = _broker.Subscribe("device-2", empresa, loja, "dev-2");

        await _broker.NotifyMutationsAppliedAsync(empresa, loja, "dev-1", 3);

        console.Slot.Queue.Should().BeEmpty();
        outroDevice.Slot.Queue.Should().ContainSingle().Which.ParaFrame().Should()
            .StartWith("data: {\"type\":\"mutations-applied\"", "o PWA escuta onmessage, sem nome de evento");
    }

    [Fact]
    public void DisposeRemoveOuvinte()
    {
        var empresa = Guid.NewGuid();
        var ouvinte = _broker.SubscribeOperacao("a", empresa);
        ouvinte.Dispose();

        _broker.PublicarOperacao(empresa, "pedido.pago", new { });

        ouvinte.Slot.Queue.Should().BeEmpty();
        ouvinte.Slot.Cancelled.Should().BeTrue();
    }
}
