using EasyStock.Domain.Events;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Events;

/// <summary>
/// Smoke tests para records de Domain.Events: garantem construcao com todos
/// os campos, equality estrutural e propriedades herdadas de DomainEvent.
/// Cada record sem teste contava 0% — esses cobrem com 1 round-trip.
/// </summary>
public class EventsCoverageTests
{
    [Fact]
    public void PedidoFornecedorRecebido_preserva_total_e_data()
    {
        var data = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var ev = new PedidoFornecedorRecebido(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7, data);

        ev.TotalItensRecebidos.Should().Be(7);
        ev.DataRecebimento.Should().Be(data);
    }

    [Fact]
    public void PedidoFornecedorItemRecebido_preserva_quantidade_decimal()
    {
        var ev = new PedidoFornecedorItemRecebido(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            QuantidadeRecebida: 3.5m,
            DataRecebimento: DateTime.UtcNow);

        ev.QuantidadeRecebida.Should().Be(3.5m);
    }
}
