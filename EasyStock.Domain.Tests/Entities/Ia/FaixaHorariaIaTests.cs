using EasyStock.Domain.Entities.Ia;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Ia;

/// <summary>#3064 (S2b de #2948, D7): pico de 01h a 04h e de 06h a 10h UTC, de segunda a sexta; o resto é fora do pico.</summary>
public class FaixaHorariaIaTests
{
    [Theory]
    [InlineData("2026-11-23T01:00:00Z", false)] // segunda, início do pico
    [InlineData("2026-11-23T03:59:59Z", false)]
    [InlineData("2026-11-23T04:00:00Z", true)]
    [InlineData("2026-11-23T05:30:00Z", true)]
    [InlineData("2026-11-23T06:00:00Z", false)]
    [InlineData("2026-11-23T09:59:59Z", false)]
    [InlineData("2026-11-23T10:00:00Z", true)]
    [InlineData("2026-11-23T00:59:59Z", true)]
    [InlineData("2026-11-27T07:00:00Z", false)] // sexta
    [InlineData("2026-11-21T07:00:00Z", true)]  // sábado
    [InlineData("2026-11-22T02:00:00Z", true)]  // domingo
    public void ForaDoPicoSegueAJanelaUtc(string instante, bool foraDoPico)
    {
        FaixaHorariaIa.ForaDoPico(DateTime.Parse(instante, null, System.Globalization.DateTimeStyles.AdjustToUniversal))
            .Should().Be(foraDoPico);
    }

    [Fact]
    public void HorarioLocalEConvertidoParaUtc()
    {
        // 07h em Brasília (UTC-3) de segunda = 10h UTC: fora do pico.
        var local = new DateTimeOffset(2026, 11, 23, 7, 0, 0, TimeSpan.FromHours(-3));
        FaixaHorariaIa.ForaDoPico(local.UtcDateTime).Should().BeTrue();
    }
}
