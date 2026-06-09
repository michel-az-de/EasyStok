using EasyStock.Api.Startup;
using EasyStock.Application.Common;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Startup;

public class StartupHardeningTimezoneTests
{
    [Fact]
    public void Producao_com_fuso_degradado_recusa_subir()
    {
        var act = () => StartupHardening.ValidateTimezoneCore(isProduction: true, FonteFuso.FallbackFixo, -180);
        act.Should().Throw<InvalidOperationException>().WithMessage("*tzdata*");
    }

    [Fact]
    public void Producao_com_offset_implausivel_recusa_subir()
    {
        var act = () => StartupHardening.ValidateTimezoneCore(isProduction: true, FonteFuso.Iana, 0);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Producao_com_fuso_iana_correto_sobe()
    {
        var act = () => StartupHardening.ValidateTimezoneCore(isProduction: true, FonteFuso.Iana, -180);
        act.Should().NotThrow();
    }

    [Fact]
    public void Dev_tolera_fuso_degradado_e_sobe()
    {
        var act = () => StartupHardening.ValidateTimezoneCore(isProduction: false, FonteFuso.FallbackFixo, -180);
        act.Should().NotThrow();
    }
}
