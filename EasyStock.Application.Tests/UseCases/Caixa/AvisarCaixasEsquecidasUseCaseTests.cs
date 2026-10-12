using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Caixa;

/// <summary>
/// N11: caracterização do miolo extraído do <c>CaixaEsquecidoJob</c> (o sino segue como era) e o
/// <c>PrazoEstourado</c> de caixa esquecido com chave determinística.
/// </summary>
public class AvisarCaixasEsquecidasUseCaseTests
{
    // 10:00 UTC de 02/10/2026 = 07:00 em Brasília; a abertura é de 01/10 às 11:00 UTC.
    private static readonly DateTime Agora = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICaixaRepository _caixa = Substitute.For<ICaixaRepository>();
    private readonly IRotinaRepository _rotinas = Substitute.For<IRotinaRepository>();
    private readonly IEventoNotificacaoRepository _eventos = Substitute.For<IEventoNotificacaoRepository>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly PrazosOptions _opcoes = new();
    private readonly HashSet<string> _gravados = [];
    private readonly MovimentoCaixa _abertura;
    private readonly Guid _quemAbriu = Guid.NewGuid();

    public AvisarCaixasEsquecidasUseCaseTests()
    {
        _abertura = MovimentoCaixa.Criar(_empresaId, "abertura", 100m, new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc));
        _abertura.RegistradoPorUserId = _quemAbriu;
        _rotinas.ExisteAtivaAsync(TipoEventoNotificacao.CaixaAbertoEsquecido, Arg.Any<CancellationToken>()).Returns(true);
        _caixa.GetAberturasEsquecidasAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ => new[] { _abertura });
        _eventos.ExisteCorrelacaoAsync(_empresaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => _gravados.Contains((string)ci[1]));
        _notificador
            .When(n => n.EnfileirarEventoAsync(Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>()))
            .Do(ci => _gravados.Add((string)ci[5]));
    }

    private AvisarCaixasEsquecidasUseCase UseCase() =>
        new(_caixa, _rotinas, _eventos, _notificador, _uow, Options.Create(_opcoes),
            new FakeTimeProvider(new DateTimeOffset(Agora)), NullLogger<AvisarCaixasEsquecidasUseCase>.Instance);

    [Fact]
    public async Task SemMudarComportamentoPublicaCaixaAbertoEsquecido()
    {
        var avisados = await UseCase().ExecuteAsync();

        avisados.Should().Be(1);
        await _notificador.Received(1).PublicarEventoAsync(
            TipoEventoNotificacao.CaixaAbertoEsquecido, _empresaId, _quemAbriu,
            Arg.Is<string>(p => p.Contains("\"data_abertura\":\"01/10/2026\"") && p.Contains(_abertura.Id.ToString())),
            Arg.Any<IDictionary<string, object?>?>(), Arg.Any<CancellationToken>());
        await _caixa.Received(1).MarcarNotificadoEsquecidoAsync(_abertura.Id, Agora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JaNotificadoOuSemRotinaOuSemDestinatarioNaoAvisa()
    {
        _abertura.NotificadoEsquecidoEm = Agora.AddDays(-1);
        (await UseCase().ExecuteAsync()).Should().Be(0);

        _abertura.NotificadoEsquecidoEm = null;
        _abertura.RegistradoPorUserId = null;
        _caixa.ResolverResponsavelPadraoAsync(_empresaId, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        (await UseCase().ExecuteAsync()).Should().Be(0);

        _rotinas.ExisteAtivaAsync(TipoEventoNotificacao.CaixaAbertoEsquecido, Arg.Any<CancellationToken>()).Returns(false);
        (await UseCase().ExecuteAsync()).Should().Be(0);

        await _notificador.DidNotReceiveWithAnyArgs().PublicarEventoAsync(default, default, default, default!, default, default);
        await _caixa.DidNotReceiveWithAnyArgs().MarcarNotificadoEsquecidoAsync(default, default, default);
    }

    [Fact]
    public async Task QuemAbriuDesconhecidoUsaOResponsavelPadrao()
    {
        var dono = Guid.NewGuid();
        _abertura.RegistradoPorUserId = null;
        _caixa.ResolverResponsavelPadraoAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(dono);

        await UseCase().ExecuteAsync();

        await _notificador.Received(1).PublicarEventoAsync(
            TipoEventoNotificacao.CaixaAbertoEsquecido, _empresaId, dono, Arg.Any<string>(),
            Arg.Any<IDictionary<string, object?>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TambemEnfileiraPrazoEstouradoComChaveDeterministica()
    {
        await UseCase().ExecuteAsync();

        var chamada = _notificador.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(INotificadorService.EnfileirarEventoAsync));
        chamada.GetArguments()[0].Should().Be(TipoEventoNotificacao.PrazoEstourado);
        chamada.GetArguments()[5].Should().Be($"prazo:caixa_esquecido:{_abertura.Id:N}");
        var payload = JsonDocument.Parse((string)chamada.GetArguments()[2]!).RootElement;
        payload.GetProperty("tipo_legivel").GetString().Should().Be("Caixa aberto de ontem");
        payload.GetProperty("prazo_texto").GetString().Should().Be("01/10/2026");
        payload.GetProperty("atraso_texto").GetString().Should().Be("1 dia");
        payload.TryGetProperty("usuarioId", out _).Should().BeFalse("a audiência gestores decide");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task QuedaEntreOSinoECarimboNaoRepeteOAvisoExterno()
    {
        // Primeiro passe: o sino sai e o carimbo falha (processo caiu). O PrazoEstourado já foi commitado.
        _caixa.MarcarNotificadoEsquecidoAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("caiu")));
        await FluentActions.Awaiting(() => UseCase().ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();

        // Segundo passe, no dia seguinte: o sino repete (at-least-once), o aviso externo não.
        _caixa.MarcarNotificadoEsquecidoAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        await UseCase().ExecuteAsync();

        _notificador.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(INotificadorService.EnfileirarEventoAsync))
            .Should().Be(1);
    }

    [Fact]
    public async Task InterruptorDesligadoMantemOSinoSemPrazo()
    {
        _opcoes.Habilitado = false;

        await UseCase().ExecuteAsync();

        await _notificador.Received(1).PublicarEventoAsync(
            TipoEventoNotificacao.CaixaAbertoEsquecido, _empresaId, _quemAbriu, Arg.Any<string>(),
            Arg.Any<IDictionary<string, object?>?>(), Arg.Any<CancellationToken>());
        _notificador.ReceivedCalls()
            .Should().NotContain(c => c.GetMethodInfo().Name == nameof(INotificadorService.EnfileirarEventoAsync));
    }
}
