using EasyStock.Api.Mobile.Services.Linkers;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #3328: o CriarPedidoUseCase recusa agenda no passado, e a venda do aparelho cuja hora marcada passou antes do
/// envio (o APK chegou a atrasar de 5 a 29 dias) nunca virava Pedido. O vínculo leva ao ERP só a agenda que
/// ainda vai acontecer; a que venceu segue sem agendamento.
/// </summary>
public sealed class AgendaDoPedidoDoAparelhoTests
{
    private static readonly DateTime Agora = new(2026, 11, 25, 14, 16, 0, DateTimeKind.Utc);

    [Fact]
    public void AgendaFutura_SegueAoPedido()
    {
        var agenda = Agora.AddHours(3);

        OrderLinker.AgendaQueAindaVaiAcontecer(agenda, Agora).Should().Be(agenda);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-60 * 24 * 29)]
    public void AgendaVencida_ViraSemAgenda(int minutosDepoisDeAgora)
    {
        OrderLinker.AgendaQueAindaVaiAcontecer(Agora.AddMinutes(minutosDepoisDeAgora), Agora).Should().BeNull();
    }

    [Fact]
    public void AgendaNoInstanteDeAgora_ViraSemAgenda()
    {
        // O use case recusa "<= agora": o limite cai do lado de quem não agenda.
        OrderLinker.AgendaQueAindaVaiAcontecer(Agora, Agora).Should().BeNull();
    }

    [Fact]
    public void SemAgenda_ContinuaSemAgenda()
    {
        OrderLinker.AgendaQueAindaVaiAcontecer(null, Agora).Should().BeNull();
    }

    [Fact]
    public void AgendaSemKind_EhLidaComoUtc_ComoFazOUseCase()
    {
        var vencida = DateTime.SpecifyKind(Agora.AddDays(-1), DateTimeKind.Unspecified);
        var futura = DateTime.SpecifyKind(Agora.AddDays(1), DateTimeKind.Unspecified);

        OrderLinker.AgendaQueAindaVaiAcontecer(vencida, Agora).Should().BeNull();
        OrderLinker.AgendaQueAindaVaiAcontecer(futura, Agora).Should().Be(Agora.AddDays(1));
    }
}
