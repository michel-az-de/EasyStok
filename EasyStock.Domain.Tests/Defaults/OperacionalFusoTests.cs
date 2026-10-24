using EasyStock.Domain.Defaults;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Defaults;

/// <summary>#2018: o fuso de Brasília tem um dono só (Domain); Application, a agenda da loja e os avisos de conta delegam a ele.</summary>
public class OperacionalFusoTests
{
    [Fact]
    public void SemIanaNemWindows_CaiNoFixoMenos3_SemLancar()
    {
        var (tz, fonte) = OperacionalFuso.Resolver(_ => throw new TimeZoneNotFoundException());

        fonte.Should().Be(FonteFuso.FallbackFixo);
        tz.BaseUtcOffset.Should().Be(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void SoComWindows_UsaWindows()
    {
        var sentinela = TimeZoneInfo.CreateCustomTimeZone("sentinela", TimeSpan.FromHours(-3), "s", "s");

        var (tz, fonte) = OperacionalFuso.Resolver(id =>
            id == OperacionalDefaults.Timezone ? throw new InvalidTimeZoneException() : sentinela);

        fonte.Should().Be(FonteFuso.Windows);
        tz.Should().BeSameAs(sentinela);
    }

    [Fact]
    public void AmbienteDeTeste_ResolveOFusoRealSemDegradar() =>
        OperacionalFuso.Fonte.Should().NotBe(FonteFuso.FallbackFixo);

    [Fact]
    public void DataOperacional_NaJanelaNoturnaAindaEODiaDeBrasilia()
    {
        // 2026-06-10T02:30Z = 09/06 23:30 em Brasília: o servidor em UTC já está no dia 10.
        var noiteUtc = new DateTime(2026, 6, 10, 2, 30, 0, DateTimeKind.Utc);

        OperacionalFuso.DataOperacional(noiteUtc).Should().Be(new DateOnly(2026, 6, 9));
        OperacionalFuso.DataOperacional(DateTime.SpecifyKind(noiteUtc, DateTimeKind.Unspecified)).Should().Be(new DateOnly(2026, 6, 9));
        OperacionalFuso.ParaBrasilia(noiteUtc).Should().Be(new DateTime(2026, 6, 9, 23, 30, 0));
    }
}
