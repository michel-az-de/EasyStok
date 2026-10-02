using EasyStock.Application.Services.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

public class AvaliadorIncidenteTests
{
    private static readonly DateTime T0 = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly LimiaresIncidente Limiares = LimiaresIncidente.Endpoint;

    private static DecisaoIncidente Falha(EndpointHealthState e, int minuto) =>
        AvaliadorIncidente.Avaliar(e, saudavel: false, T0.AddMinutes(minuto), Limiares).Decisao;

    private static AvaliacaoIncidente Boa(EndpointHealthState e, int minuto) =>
        AvaliadorIncidente.Avaliar(e, saudavel: true, T0.AddMinutes(minuto), Limiares);

    [Fact]
    public void TerceiraFalhaAbre()
    {
        var e = new EndpointHealthState { EndpointName = "api" };

        Falha(e, 0).Should().Be(DecisaoIncidente.Nada);
        Falha(e, 1).Should().Be(DecisaoIncidente.Nada);
        var terceira = AvaliadorIncidente.Avaliar(e, false, T0.AddMinutes(2), Limiares);

        terceira.Decisao.Should().Be(DecisaoIncidente.Abrir);
        terceira.DesdeUtc.Should().Be(T0.AddMinutes(2));
        e.LastAlertedAt.Should().Be(T0.AddMinutes(2));
    }

    [Fact]
    public void FalhaIsoladaNaoAbre()
    {
        var e = new EndpointHealthState { EndpointName = "api" };

        Falha(e, 0).Should().Be(DecisaoIncidente.Nada);
        Boa(e, 1).Decisao.Should().Be(DecisaoIncidente.Nada);
        Falha(e, 2).Should().Be(DecisaoIncidente.Nada);

        e.LastAlertedAt.Should().BeNull();
        e.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public void AbertoReavisa()
    {
        var e = new EndpointHealthState { EndpointName = "api" };
        for (var i = 0; i < 3; i++) Falha(e, i);

        var depois = AvaliadorIncidente.Avaliar(e, false, T0.AddMinutes(3), Limiares);

        depois.Decisao.Should().Be(DecisaoIncidente.Reavisar);
        depois.DesdeUtc.Should().Be(T0.AddMinutes(2), "o incidente abriu na terceira falha, o reaviso não muda o início");
    }

    [Fact]
    public void DuasBoasResolvem()
    {
        var e = new EndpointHealthState { EndpointName = "api" };
        for (var i = 0; i < 3; i++) Falha(e, i);

        Boa(e, 10).Decisao.Should().Be(DecisaoIncidente.Nada);
        var segunda = Boa(e, 11);

        segunda.Decisao.Should().Be(DecisaoIncidente.Resolver);
        segunda.DesdeUtc.Should().Be(T0.AddMinutes(2), "a duração do aviso conta da abertura");
        e.LastAlertedAt.Should().BeNull();
        e.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void BoaIntercaladaPorFalhaNaoResolve()
    {
        var e = new EndpointHealthState { EndpointName = "api" };
        for (var i = 0; i < 3; i++) Falha(e, i);

        Boa(e, 10);
        Falha(e, 11).Should().Be(DecisaoIncidente.Reavisar);
        Boa(e, 12).Decisao.Should().Be(DecisaoIncidente.Nada);
        Boa(e, 13).Decisao.Should().Be(DecisaoIncidente.Resolver);
    }

    [Fact]
    public void RecuperacaoSemIncidenteAbertoNaoAvisa()
    {
        var e = new EndpointHealthState { EndpointName = "api" };
        Falha(e, 0);
        Falha(e, 1);

        Boa(e, 2).Decisao.Should().Be(DecisaoIncidente.Nada);
        Boa(e, 3).Decisao.Should().Be(DecisaoIncidente.Nada);
        e.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void LimiaresDoBancoAbremEmDuasEResolvemEmTres()
    {
        var e = new EndpointHealthState { EndpointName = "sistema/db" };
        var banco = new LimiaresIncidente(2, 3);

        AvaliadorIncidente.Avaliar(e, false, T0, banco).Decisao.Should().Be(DecisaoIncidente.Nada);
        AvaliadorIncidente.Avaliar(e, false, T0.AddMinutes(1), banco).Decisao.Should().Be(DecisaoIncidente.Abrir);
        AvaliadorIncidente.Avaliar(e, true, T0.AddMinutes(2), banco).Decisao.Should().Be(DecisaoIncidente.Nada);
        AvaliadorIncidente.Avaliar(e, true, T0.AddMinutes(3), banco).Decisao.Should().Be(DecisaoIncidente.Nada);
        AvaliadorIncidente.Avaliar(e, true, T0.AddMinutes(4), banco).Decisao.Should().Be(DecisaoIncidente.Resolver);
    }
}
