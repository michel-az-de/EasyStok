using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N13: teto de 10 disparos de teste por hora por superadmin, em memória (teto de segurança, não cota contábil).</summary>
public class LimitadorDisparoTesteTests
{
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _superadmin = Guid.NewGuid();

    private LimitadorDisparoTeste Criar() => new(_relogio);

    [Fact]
    public void DecimoPrimeiroNaHoraEhRecusado()
    {
        var limitador = Criar();

        for (var i = 0; i < 10; i++)
        {
            limitador.TentarAdquirir(_superadmin, out _).Should().BeTrue($"o disparo {i + 1} cabe na hora");
            _relogio.Advance(TimeSpan.FromMinutes(1));
        }

        limitador.TentarAdquirir(_superadmin, out var esperar).Should().BeFalse();
        esperar.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromHours(1));
    }

    [Fact]
    public void UmaHoraDepoisLibera()
    {
        var limitador = Criar();
        for (var i = 0; i < 10; i++) limitador.TentarAdquirir(_superadmin, out _);
        limitador.TentarAdquirir(_superadmin, out _).Should().BeFalse();

        _relogio.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        limitador.TentarAdquirir(_superadmin, out _).Should().BeTrue();
    }

    [Fact]
    public void ContaPorSuperadmin()
    {
        var limitador = Criar();
        for (var i = 0; i < 10; i++) limitador.TentarAdquirir(_superadmin, out _);

        limitador.TentarAdquirir(Guid.NewGuid(), out _).Should().BeTrue();
    }

    [Fact]
    public void RecusaNaoConsomeCota()
    {
        var limitador = Criar();
        for (var i = 0; i < 10; i++) limitador.TentarAdquirir(_superadmin, out _);

        for (var i = 0; i < 5; i++) limitador.TentarAdquirir(_superadmin, out _).Should().BeFalse();
        _relogio.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        limitador.TentarAdquirir(_superadmin, out _).Should().BeTrue();
    }
}
