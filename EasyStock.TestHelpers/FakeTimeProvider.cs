namespace EasyStock.TestHelpers;

/// <summary>
/// <see cref="TimeProvider"/> fake mínimo para testes determinísticos.
///
/// <para>
/// Evita dep externa em <c>Microsoft.Extensions.TimeProvider.Testing</c> e centraliza
/// o helper já replicado em <c>EasyStock.Domain.Tests</c> (internal). Aqui é
/// <see langword="public"/> para uso transversal de Application.Tests / Api.IntegrationTests.
/// </para>
///
/// <para>
/// Uso típico:
/// <code>
/// var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-24T10:00:00Z"));
/// var useCase = new XxxUseCase(..., time, ...);
/// time.Advance(TimeSpan.FromMinutes(6));
/// </code>
/// </para>
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Avança o relógio em <paramref name="delta"/>.</summary>
    public void Advance(TimeSpan delta)
    {
        _now = _now.Add(delta);
    }

    /// <summary>Salta o relógio para <paramref name="instant"/> (absoluto).</summary>
    public void SetUtcNow(DateTimeOffset instant)
    {
        _now = instant;
    }
}
