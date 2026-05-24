namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// TimeProvider fake mínimo para testes determinísticos. Evita dep externa em
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> para manter Domain.Tests
/// com surface mínima (xunit + FluentAssertions + NSubstitute).
///
/// Uso típico:
/// <code>
/// var time = new FakeTime(DateTimeOffset.Parse("2026-05-24T10:00:00Z"));
/// var otp  = ClienteOtp.Criar(..., time, ...);
/// time.Advance(TimeSpan.FromMinutes(6));
/// otp.Expirou(time).Should().BeTrue();
/// </code>
/// </summary>
internal sealed class FakeTime : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTime(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta)
    {
        _now = _now.Add(delta);
    }
}
