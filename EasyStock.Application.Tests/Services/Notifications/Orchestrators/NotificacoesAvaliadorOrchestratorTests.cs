using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.Services.Notifications.Orchestrators;

public class NotificacoesAvaliadorOrchestratorTests
{
    [Fact]
    public async Task ExecutarRodadaAsync_sem_pendentes_nao_chama_notificador()
    {
        var notificador = Substitute.For<INotificadorService>();
        var eventoRepo = Substitute.For<IEventoNotificacaoRepository>();
        eventoRepo.ListarPendentesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<EventoNotificacao>)Array.Empty<EventoNotificacao>());
        var rotinaRepo = Substitute.For<IRotinaRepository>();
        rotinaRepo.ListarAtivasAsync(Arg.Any<TipoEventoNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RotinaNotificacao>)Array.Empty<RotinaNotificacao>());

        var sut = new NotificacoesAvaliadorOrchestrator(
            notificador, eventoRepo, rotinaRepo, new RotinaScheduler(),
            NullLogger<NotificacoesAvaliadorOrchestrator>.Instance);

        await sut.ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        await notificador.DidNotReceiveWithAnyArgs().AvaliarEventoAsync(default!, default);
    }

    [Fact]
    public async Task ExecutarRodadaAsync_processa_todos_os_eventos_pendentes()
    {
        var ev1 = EventoNotificacao.Criar(TipoEventoNotificacao.ProdutoVencendo, Guid.NewGuid(), "{}");
        var ev2 = EventoNotificacao.Criar(TipoEventoNotificacao.AssinaturaExpirando, Guid.NewGuid(), "{}");

        var notificador = Substitute.For<INotificadorService>();
        var eventoRepo = Substitute.For<IEventoNotificacaoRepository>();
        eventoRepo.ListarPendentesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<EventoNotificacao>)new[] { ev1, ev2 });
        var rotinaRepo = Substitute.For<IRotinaRepository>();
        rotinaRepo.ListarAtivasAsync(Arg.Any<TipoEventoNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RotinaNotificacao>)Array.Empty<RotinaNotificacao>());

        var sut = new NotificacoesAvaliadorOrchestrator(
            notificador, eventoRepo, rotinaRepo, new RotinaScheduler(),
            NullLogger<NotificacoesAvaliadorOrchestrator>.Instance);

        await sut.ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        await notificador.Received(1).AvaliarEventoAsync(ev1, Arg.Any<CancellationToken>());
        await notificador.Received(1).AvaliarEventoAsync(ev2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecutarRodadaAsync_evento_com_erro_nao_interrompe_demais()
    {
        // Defesa contra "evento veneno" — falha em um não pode bloquear os outros 199 da página
        var ev1 = EventoNotificacao.Criar(TipoEventoNotificacao.ProdutoVencendo, Guid.NewGuid(), "{}");
        var ev2 = EventoNotificacao.Criar(TipoEventoNotificacao.AssinaturaExpirando, Guid.NewGuid(), "{}");
        var ev3 = EventoNotificacao.Criar(TipoEventoNotificacao.TarefaPendente, Guid.NewGuid(), "{}");

        var notificador = Substitute.For<INotificadorService>();
        notificador.AvaliarEventoAsync(ev2, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("simulado"));

        var eventoRepo = Substitute.For<IEventoNotificacaoRepository>();
        eventoRepo.ListarPendentesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<EventoNotificacao>)new[] { ev1, ev2, ev3 });
        var rotinaRepo = Substitute.For<IRotinaRepository>();
        rotinaRepo.ListarAtivasAsync(Arg.Any<TipoEventoNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RotinaNotificacao>)Array.Empty<RotinaNotificacao>());

        var sut = new NotificacoesAvaliadorOrchestrator(
            notificador, eventoRepo, rotinaRepo, new RotinaScheduler(),
            NullLogger<NotificacoesAvaliadorOrchestrator>.Instance);

        var act = async () => await sut.ExecutarRodadaAsync(TimeSpan.FromMinutes(2));
        await act.Should().NotThrowAsync();

        await notificador.Received(1).AvaliarEventoAsync(ev1, Arg.Any<CancellationToken>());
        await notificador.Received(1).AvaliarEventoAsync(ev2, Arg.Any<CancellationToken>());
        await notificador.Received(1).AvaliarEventoAsync(ev3, Arg.Any<CancellationToken>()); // continuou após ev2 falhar
    }
}
