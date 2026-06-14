using EasyStock.Web.Models.Api;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Models;

/// <summary>Projeção PedidoRowDto.From — campos + computados (Pendente/Quitado/IsAtrasado) (#591).</summary>
public class PedidoRowDtoTests
{
    [Fact]
    public void From_projeta_campos_e_computados()
    {
        var p = new Pedido
        {
            Id = "p1",
            Status = "pronto",
            ClienteNome = "Maria",
            Total = 100m,
            TotalPago = 40m,
            ItensCount = 3,
            AgendadoParaEm = DateTime.UtcNow.AddHours(-1) // agendado no passado
        };

        var dto = PedidoRowDto.From(p);

        dto.Id.Should().Be("p1");
        dto.ClienteNome.Should().Be("Maria");
        dto.ItensCount.Should().Be(3);
        dto.Pendente.Should().Be(60m);
        dto.Quitado.Should().BeFalse();
        dto.IsScheduled.Should().BeTrue();
        dto.IsAtrasado.Should().BeTrue(); // agendado vencido + status não-terminal
    }

    [Fact]
    public void Quitado_quando_pago_cobre_total()
    {
        var dto = PedidoRowDto.From(new Pedido
        {
            Id = "p", Status = "entregue", Total = 50m, TotalPago = 50m
        });

        dto.Quitado.Should().BeTrue();
        dto.Pendente.Should().Be(0m);
    }
}
