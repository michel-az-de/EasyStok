using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>
/// N10: nenhum aviso de incidente carrega mensagem de exceção, query string, e-mail ou nome de cliente. O estado de saúde
/// recebe sentinelas; o payload serializado e o texto renderizado de cada canal não podem contê-las.
/// </summary>
public class IncidentesNaoVazamDadoTests
{
    private const string Email = "maria.cliente@exemplo.com";
    private const string Url = "https://api.exemplo.com/pedidos?token=SEGREDO123";
    private const string Cliente = "Joaquina Sentinela da Silva";

    private static readonly string[] Sentinelas = [Email, "SEGREDO123", "token=", Cliente, "exemplo.com"];

    [Fact]
    public async Task AlertaNaoCarregaExcecaoNemQueryStringNemDadoDeCliente()
    {
        var excecao = new InvalidOperationException($"falha para {Email} em {Url} cliente {Cliente}");
        var estado = new EndpointHealthState { EndpointName = "api/mobile/version" };
        var codigo = CodigoFalhaIncidente.DeExcecao(excecao);
        estado.LastFailureMessage = codigo;
        for (var i = 0; i < 3; i++) AvaliadorIncidente.Avaliar(estado, false, DateTime.UtcNow, LimiaresIncidente.Endpoint);

        string? payload = null;
        var notificador = Substitute.For<INotificadorService>();
        notificador.EnfileirarEventoAsync(
                Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(c => { payload = c.ArgAt<string>(2); return Guid.NewGuid(); });
        var empresa = Substitute.For<IEmpresaPadraoResolver>();
        empresa.ResolverAsync(Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var publicador = new PublicadorIncidenteSistema(
            new ConfigurationBuilder().Build(), empresa, notificador, Substitute.For<ITenantContextAccessor>(),
            Substitute.For<IUnitOfWork>(), TimeProvider.System, NullLogger<PublicadorIncidenteSistema>.Instance);

        await publicador.PublicarAsync(
            ComponenteIncidente.Api, EstadoIncidente.ComProblema, SeveridadeIncidente.Alta, estado.LastAlertedAt!.Value);

        estado.LastFailureMessage.Should().BeOneOf("TIMEOUT", "CONEXAO_RECUSADA", "OUTRO").And.NotContainAny(Sentinelas);
        payload.Should().NotBeNull();
        payload.Should().NotContainAny(Sentinelas);

        // O texto que o destinatário vê é o do template renderizado só com as variáveis do payload.
        var vars = JsonDocument.Parse(payload!).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var assunto = $"EasyStok: {vars["componente"]} {vars["estado_texto"]}";
        var corpoWhatsApp = $"EasyStok informa: o componente {vars["componente"]} está {vars["estado_texto"]} desde {vars["desde"]}.";
        var corpoEmail = $"{vars["componente"]} {vars["estado_texto"]} {vars["gravidade"]} {vars["desde"]} {vars["duracao"]}";
        foreach (var texto in new[] { assunto, corpoWhatsApp, corpoEmail })
            texto.Should().NotContainAny(Sentinelas);
    }

    [Theory]
    [InlineData(typeof(TimeoutException), "TIMEOUT")]
    [InlineData(typeof(TaskCanceledException), "TIMEOUT")]
    [InlineData(typeof(HttpRequestException), "CONEXAO_RECUSADA")]
    [InlineData(typeof(InvalidOperationException), "OUTRO")]
    public void CodigoDeFalhaEFechado(Type tipo, string esperado)
    {
        var ex = (Exception)Activator.CreateInstance(tipo, "msg com dado de cliente maria@exemplo.com")!;

        CodigoFalhaIncidente.DeExcecao(ex).Should().Be(esperado);
        CodigoFalhaIncidente.DeStatusHttp(503).Should().Be("HTTP_503");
    }
}
