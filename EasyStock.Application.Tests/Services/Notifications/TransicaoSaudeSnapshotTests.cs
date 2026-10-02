using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

public class TransicaoSaudeSnapshotTests
{
    private static readonly DateTime T0 = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<TransicaoSaude> Passo(AvaliadorSaudeSnapshot a, int minuto, string geral, string? redis = "ok") =>
        a.Avaliar(geral, redis, T0.AddMinutes(minuto));

    [Fact]
    public void CriticalAbreEOkResolve()
    {
        var a = new AvaliadorSaudeSnapshot();

        Passo(a, 0, "critical").Should().BeEmpty();
        var abre = Passo(a, 1, "critical").Should().ContainSingle().Subject;
        abre.Componente.Should().Be(ComponenteIncidente.Banco);
        abre.Decisao.Should().Be(DecisaoIncidente.Abrir);
        abre.Severidade.Should().Be(SeveridadeIncidente.Critica);

        Passo(a, 2, "ok").Should().BeEmpty();
        Passo(a, 3, "ok").Should().BeEmpty();
        var resolve = Passo(a, 4, "ok").Should().ContainSingle().Subject;
        resolve.Decisao.Should().Be(DecisaoIncidente.Resolver);
        resolve.DesdeUtc.Should().Be(T0.AddMinutes(1));
    }

    [Fact]
    public void DegradadoPorErrosNaoAlerta()
    {
        var a = new AvaliadorSaudeSnapshot();

        for (var i = 0; i < 10; i++)
            Passo(a, i, "degraded", redis: "ok").Should().BeEmpty("degraded só por ErrorCount é coberto pelo pico de 5xx");
    }

    [Fact]
    public void DegradadoPorRedisAlerta()
    {
        var a = new AvaliadorSaudeSnapshot();

        Passo(a, 0, "degraded", "falha").Should().BeEmpty();
        var abre = Passo(a, 1, "degraded", "falha").Should().ContainSingle().Subject;
        abre.Componente.Should().Be(ComponenteIncidente.Redis);
        abre.Decisao.Should().Be(DecisaoIncidente.Abrir);

        Passo(a, 2, "ok").Should().BeEmpty();
        Passo(a, 3, "ok").Should().BeEmpty();
        Passo(a, 4, "ok").Should().ContainSingle().Which.Decisao.Should().Be(DecisaoIncidente.Resolver);
    }

    [Fact]
    public void BlipDeUmSnapshotNaoAbre()
    {
        var a = new AvaliadorSaudeSnapshot();

        Passo(a, 0, "critical").Should().BeEmpty();
        Passo(a, 1, "ok").Should().BeEmpty();
        Passo(a, 2, "critical").Should().BeEmpty();
        Passo(a, 3, "ok", redis: "falha").Should().BeEmpty();
        Passo(a, 4, "ok").Should().BeEmpty();
    }

    [Fact]
    public void RedisNaoConfiguradoNaoAlerta()
    {
        var a = new AvaliadorSaudeSnapshot();

        for (var i = 0; i < 5; i++) Passo(a, i, "ok", redis: null).Should().BeEmpty();
    }
}
