using System.Reflection;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.Services.Notifications;

public class PublicadorIncidenteSistemaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly IEmpresaPadraoResolver _empresaPadrao = Substitute.For<IEmpresaPadraoResolver>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<PublicadorIncidenteSistema> _logger = Substitute.For<ILogger<PublicadorIncidenteSistema>>();
    private readonly List<(string? Payload, string? Correlacao, TipoEventoNotificacao Tipo)> _publicados = [];

    public PublicadorIncidenteSistemaTests()
    {
        _empresaPadrao.ResolverAsync(Arg.Any<CancellationToken>()).Returns(Empresa);
        _notificador.EnfileirarEventoAsync(
                Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(c =>
            {
                _publicados.Add((c.ArgAt<string>(2), c.ArgAt<string?>(5), c.ArgAt<TipoEventoNotificacao>(0)));
                return Guid.NewGuid();
            });
    }

    private PublicadorIncidenteSistema Criar(Dictionary<string, string?>? config = null) => new(
        new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build(),
        _empresaPadrao, _notificador, _tenant, _uow, _relogio, _logger);

    private Task Publicar(PublicadorIncidenteSistema p, EstadoIncidente estado = EstadoIncidente.ComProblema) =>
        p.PublicarAsync(ComponenteIncidente.Api, estado, SeveridadeIncidente.Alta, _relogio.GetUtcNow().UtcDateTime.AddMinutes(-12));

    [Fact]
    public async Task PayloadSoTemChavesFechadas()
    {
        await Publicar(Criar());

        var publicado = _publicados.Should().ContainSingle().Subject;
        publicado.Tipo.Should().Be(TipoEventoNotificacao.IncidenteSistema);
        var doc = JsonDocument.Parse(publicado.Payload!);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "componente", "estado_texto", "gravidade", "desde", "duracao", "chaveIdempotencia");
        doc.RootElement.GetProperty("componente").GetString().Should().Be("API do EasyStok");
        doc.RootElement.GetProperty("estado_texto").GetString().Should().Be("com problema");
        doc.RootElement.GetProperty("gravidade").GetString().Should().Be("Alta");
        doc.RootElement.GetProperty("duracao").GetString().Should().Be("12 minutos");
        _tenant.Received(1).SetCurrentTenant(Empresa);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ChaveDeDedupeMudaNaVirada15Min()
    {
        var publicador = Criar();
        _relogio.SetUtcNow(new DateTimeOffset(2026, 10, 2, 12, 0, 1, TimeSpan.Zero)); // início da janela
        await Publicar(publicador);
        _relogio.Advance(TimeSpan.FromMinutes(14));
        await Publicar(publicador);
        _relogio.Advance(TimeSpan.FromMinutes(1));
        await Publicar(publicador);

        _publicados.Select(p => p.Correlacao).Should().HaveCount(3);
        _publicados[0].Correlacao.Should().Be(_publicados[1].Correlacao, "mesma janela de 15 min");
        _publicados[2].Correlacao.Should().NotBe(_publicados[1].Correlacao, "virou a janela");
        _publicados[0].Correlacao.Should().StartWith("incidente:api:comproblema:").And.HaveLength(
            ("incidente:api:comproblema:" + "1".PadLeft(7, '1')).Length);
        JsonDocument.Parse(_publicados[0].Payload!).RootElement.GetProperty("chaveIdempotencia").GetString()
            .Should().Be(_publicados[0].Correlacao);
    }

    [Fact]
    public async Task EstadoDiferenteTemChaveDiferenteNaMesmaJanela()
    {
        var publicador = Criar();
        await Publicar(publicador);
        await Publicar(publicador, EstadoIncidente.Normalizado);

        _publicados[0].Correlacao.Should().NotBe(_publicados[1].Correlacao);
    }

    [Fact]
    public async Task InterruptorDesligadoNaoPublica()
    {
        await Publicar(Criar(new() { [PublicadorIncidenteSistema.ChaveHabilitado] = "false" }));

        _publicados.Should().BeEmpty();
        await _empresaPadrao.DidNotReceive().ResolverAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task SemEmpresaPadraoNaoPublicaENomeiaAChave()
    {
        _empresaPadrao.ResolverAsync(Arg.Any<CancellationToken>()).Returns((Guid?)null);

        await Publicar(Criar());

        _publicados.Should().BeEmpty();
        _logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => c.GetArguments())
            .Should().Contain(a => (LogLevel)a[0]! == LogLevel.Warning
                && a[2]!.ToString()!.Contains(EmpresaPadraoResolver.Chave));
    }

    [Fact]
    public async Task JanelaConfiguravel()
    {
        var publicador = Criar(new() { [PublicadorIncidenteSistema.ChaveJanela] = "60" });
        _relogio.SetUtcNow(new DateTimeOffset(2026, 10, 2, 12, 0, 1, TimeSpan.Zero));
        await Publicar(publicador);
        _relogio.Advance(TimeSpan.FromMinutes(40));
        await Publicar(publicador);

        _publicados[0].Correlacao.Should().Be(_publicados[1].Correlacao);
    }

    [Fact]
    public void NaoExisteParametroDeTextoLivre()
    {
        var parametros = typeof(IPublicadorIncidenteSistema).GetMethods()
            .Concat(typeof(PublicadorIncidenteSistema).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetParameters());

        parametros.Should().NotContain(p => p.ParameterType == typeof(string) || p.ParameterType == typeof(object),
            "texto livre é por onde mensagem de exceção e dado de cliente entrariam no aviso");
    }
}
