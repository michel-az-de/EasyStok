using EasyStock.Application.Services.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N12: a agenda é horário de parede de Brasília, sem cron, e a conta nunca usa <c>UtcNow.Date</c>.</summary>
public class AgendaDiariaLocalTests
{
    private static readonly TimeOnly Vinte = new(20, 0);

    private static DateTime Utc(int dia, int hora, int minuto) =>
        new(2026, 10, dia, hora, minuto, 0, DateTimeKind.Utc);

    [Fact]
    public void DevidaDepoisDoHorarioNoDiaLocal()
    {
        // 23:30Z de 01/10 é 20:30 em Brasília do dia 01/10.
        AgendaDiariaLocal.Devida(Vinte, Utc(1, 23, 30)).Should().BeTrue();
        // Exatamente no horário também.
        AgendaDiariaLocal.Devida(Vinte, Utc(1, 23, 0)).Should().BeTrue();
    }

    [Fact]
    public void NaoDevidaAntesDoHorario()
    {
        // 22:59Z de 01/10 é 19:59 em Brasília.
        AgendaDiariaLocal.Devida(Vinte, Utc(1, 22, 59)).Should().BeFalse();
    }

    [Fact]
    public void ViradaDoDiaUtcNaoAdiantaNemAtrasa()
    {
        AgendaDiariaLocal.DiaLocal(Utc(1, 23, 30)).Should().Be(new DateOnly(2026, 10, 1));

        // 00:30Z de 02/10 é 21:30 em Brasília do dia 01/10: ainda é o dia 01 e a rotina das 20:00 já está devida.
        var depoisDaViradaUtc = Utc(2, 0, 30);
        AgendaDiariaLocal.DiaLocal(depoisDaViradaUtc).Should().Be(new DateOnly(2026, 10, 1));
        AgendaDiariaLocal.Devida(Vinte, depoisDaViradaUtc).Should().BeTrue();

        // 02:59Z de 02/10 ainda é 23:59 do dia 01; 03:00Z é meia-noite do dia 02.
        AgendaDiariaLocal.DiaLocal(Utc(2, 2, 59)).Should().Be(new DateOnly(2026, 10, 1));
        AgendaDiariaLocal.DiaLocal(Utc(2, 3, 0)).Should().Be(new DateOnly(2026, 10, 2));
    }

    [Fact]
    public void PerdeODiaQuandoPassouDaMeiaNoite()
    {
        // 03:10Z de 02/10 é 00:10 do dia 02 em Brasília: o horário das 20:00 do dia 02 não chegou, e o do dia 01 se perdeu.
        var depoisDaMeiaNoite = Utc(2, 3, 10);
        AgendaDiariaLocal.Devida(Vinte, depoisDaMeiaNoite).Should().BeFalse();

        // De volta às 23:50 do mesmo dia (02:50Z do dia seguinte), ainda sai no mesmo dia.
        AgendaDiariaLocal.Devida(Vinte, Utc(2, 2, 50)).Should().BeTrue();
    }

    [Fact]
    public void ChaveUsaADataLocalECabeEmSessentaEQuatroCaracteres()
    {
        var rotinaId = Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020100");

        var chave = AgendaDiariaLocal.Chave(rotinaId, AgendaDiariaLocal.DiaLocal(Utc(2, 0, 30)));

        chave.Should().Be("agenda:0f0e0d0c0b0a09080706050403020100:20261001");
        chave.Length.Should().BeLessThanOrEqualTo(64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("8:00")]
    [InlineData("24:00")]
    [InlineData("20:60")]
    [InlineData("20h00")]
    [InlineData("20:00:00")]
    [InlineData("0 20 * * *")]
    [InlineData(null)]
    public void HorarioInvalidoEhRecusado(string? texto)
    {
        AgendaDiariaLocal.TentarLerHorario(texto, out _).Should().BeFalse();
        var act = () => AgendaDiariaLocal.LerHorario(texto);
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("00:00", 0, 0)]
    [InlineData("20:00", 20, 0)]
    [InlineData("23:59", 23, 59)]
    public void HorarioValidoEhLido(string texto, int hora, int minuto)
    {
        AgendaDiariaLocal.TentarLerHorario(texto, out var horario).Should().BeTrue();
        horario.Should().Be(new TimeOnly(hora, minuto));
    }

    [Fact]
    public void LeOHorarioDeParametrosDaRotina()
    {
        AgendaDiariaLocal.HorarioDosParametros("""{"modoCanais":"todos","agenda":{"horario":"20:00"}}""")
            .Should().Be(new TimeOnly(20, 0));
        AgendaDiariaLocal.HorarioDosParametros("""{"modoCanais":"todos"}""").Should().BeNull();
        AgendaDiariaLocal.HorarioDosParametros("{}").Should().BeNull();
        AgendaDiariaLocal.HorarioDosParametros("não é json").Should().BeNull();
        AgendaDiariaLocal.HorarioDosParametros("""{"agenda":{"horario":"25:00"}}""").Should().BeNull();
    }

    [Fact]
    public void ParametrosComAgendaMalFormadaSaoDistintosDeSemAgenda()
    {
        AgendaDiariaLocal.TemAgenda("""{"agenda":{"horario":"25:00"}}""").Should().BeTrue();
        AgendaDiariaLocal.TemAgenda("""{"agenda":{}}""").Should().BeTrue();
        AgendaDiariaLocal.TemAgenda("""{"modoCanais":"todos"}""").Should().BeFalse();
        AgendaDiariaLocal.TemAgenda(null).Should().BeFalse();
    }
}
