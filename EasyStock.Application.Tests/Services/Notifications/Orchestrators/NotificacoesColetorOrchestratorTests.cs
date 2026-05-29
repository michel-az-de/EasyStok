using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications.Orchestrators;

public class NotificacoesColetorOrchestratorTests
{
    [Fact]
    public async Task ExecutarRodadaAsync_sem_coletores_registrados_nao_lanca()
    {
        var sut = new NotificacoesColetorOrchestrator(
            Array.Empty<IColetorEventoNotificacao>(),
            NullLogger<NotificacoesColetorOrchestrator>.Instance);

        var act = async () => await sut.ExecutarRodadaAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecutarRodadaAsync_executa_todos_os_coletores_em_sequencia()
    {
        var coletor1 = Substitute.For<IColetorEventoNotificacao>();
        var coletor2 = Substitute.For<IColetorEventoNotificacao>();

        var sut = new NotificacoesColetorOrchestrator(
            new[] { coletor1, coletor2 },
            NullLogger<NotificacoesColetorOrchestrator>.Instance);

        await sut.ExecutarRodadaAsync();

        await coletor1.Received(1).ColetarAsync(Arg.Any<CancellationToken>());
        await coletor2.Received(1).ColetarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecutarRodadaAsync_coletor_com_erro_nao_interrompe_demais()
    {
        var coletorOk1 = Substitute.For<IColetorEventoNotificacao>();
        var coletorErro = Substitute.For<IColetorEventoNotificacao>();
        coletorErro
            .When(x => x.ColetarAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("simulado"));
        var coletorOk2 = Substitute.For<IColetorEventoNotificacao>();

        var sut = new NotificacoesColetorOrchestrator(
            new[] { coletorOk1, coletorErro, coletorOk2 },
            NullLogger<NotificacoesColetorOrchestrator>.Instance);

        var act = async () => await sut.ExecutarRodadaAsync();

        await act.Should().NotThrowAsync(); // erro absorvido pelo orchestrator
        await coletorOk1.Received(1).ColetarAsync(Arg.Any<CancellationToken>());
        await coletorErro.Received(1).ColetarAsync(Arg.Any<CancellationToken>());
        await coletorOk2.Received(1).ColetarAsync(Arg.Any<CancellationToken>()); // continuou
    }

    [Fact]
    public async Task ExecutarRodadaAsync_propaga_cancellation_no_coletor_atual()
    {
        var coletor = Substitute.For<IColetorEventoNotificacao>();
        coletor
            .When(x => x.ColetarAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new OperationCanceledException());

        var sut = new NotificacoesColetorOrchestrator(
            new[] { coletor },
            NullLogger<NotificacoesColetorOrchestrator>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Quando ct cancelado, a exceção é re-thrown (catch when (!ct.IsCancellationRequested) não pega)
        var act = async () => await sut.ExecutarRodadaAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
