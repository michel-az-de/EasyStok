using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>#3387: o ritmo de compra (mediana de dias entre compras) numa regra só, usada pela Ficha e pelo PDF.</summary>
public class RitmoDeCompraTests
{
    private static readonly DateTime Dia1 = new(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);

    private static DateTime[] EmDias(params int[] dias) => dias.Select(d => Dia1.AddDays(d)).ToArray();

    [Fact]
    public void MenosDeTresDiasDistintosNaoTemRitmo()
    {
        RitmoDeCompra.Calcular(EmDias(0, 7)).Should().BeNull();
        RitmoDeCompra.Calcular([]).Should().BeNull();
    }

    [Fact]
    public void PedidosNoMesmoDiaContamUmaVez()
    {
        // #3387: três pedidos no mesmo dia e mais um dia dão só 2 dias: sem ritmo.
        var mesmoDia = new[] { Dia1, Dia1.AddHours(1), Dia1.AddHours(2), Dia1.AddDays(7) };
        RitmoDeCompra.Calcular(mesmoDia).Should().BeNull();

        var tres = mesmoDia.Append(Dia1.AddDays(14)).ToArray();
        RitmoDeCompra.Calcular(tres).Should().Be(new RitmoDeCompra(7, 3));
    }

    [Fact]
    public void MedianaImparUsaOValorDoMeio()
    {
        // Intervalos 2, 10, 4 → ordenados 2, 4, 10 → mediana 4.
        RitmoDeCompra.Calcular(EmDias(0, 2, 12, 16)).Should().Be(new RitmoDeCompra(4, 4));
    }

    [Fact]
    public void MedianaParArredondaMeioParaCima()
    {
        // Intervalos 2 e 5 → média 3,5 → 4 (AwayFromZero).
        RitmoDeCompra.Calcular(EmDias(0, 2, 7)).Should().Be(new RitmoDeCompra(4, 3));
    }

    [Fact]
    public void SoOsDozeDiasMaisRecentesEntram()
    {
        // 24 dias: os 12 antigos são diários; os 12 recentes, de 10 em 10. Só a janela recente dá mediana 10.
        var antigos = Enumerable.Range(0, 12);
        var recentes = Enumerable.Range(0, 12).Select(i => 100 + i * 10);
        var r = RitmoDeCompra.Calcular(EmDias(antigos.Concat(recentes).ToArray()));
        r.Should().Be(new RitmoDeCompra(10, 12));
    }

    [Fact]
    public void ODiaSegueOFusoOperacional()
    {
        // 01:00 UTC ainda é o dia anterior em São Paulo: 01:00 do dia 8 e 20:00 do dia 7 são o mesmo dia.
        var datas = new[]
        {
            new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 7, 20, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 8, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 15, 15, 0, 0, DateTimeKind.Utc),
        };
        // Dias locais: 1, 7, 7, 15 → três dias distintos, intervalos 6 e 8 → mediana 7.
        RitmoDeCompra.Calcular(datas).Should().Be(new RitmoDeCompra(7, 3));
    }
}
