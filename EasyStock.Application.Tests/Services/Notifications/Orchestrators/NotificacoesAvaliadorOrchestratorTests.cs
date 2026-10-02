using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.Services.Notifications.Orchestrators;

/// <summary>
/// O avaliador lista os eventos pendentes sob bypass (só <c>(Id, EmpresaId)</c>) e avalia cada um em escopo de DI
/// próprio, com o tenant da empresa fixado (N1). Os escopos vêm de um provider de verdade; o repositório, o notificador e
/// o tenant são substitutos compartilhados, para contar o que cada escopo fez.
/// </summary>
public class NotificacoesAvaliadorOrchestratorTests
{
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IEventoNotificacaoRepository _eventoRepo = Substitute.For<IEventoNotificacaoRepository>();
    private readonly IRotinaRepository _rotinaRepo = Substitute.For<IRotinaRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IRowLevelSecurityBypass _bypass = Substitute.For<IRowLevelSecurityBypass>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public NotificacoesAvaliadorOrchestratorTests()
    {
        _rotinaRepo.ListarAtivasAsync(Arg.Any<TipoEventoNotificacao?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RotinaNotificacao>)Array.Empty<RotinaNotificacao>());
    }

    private NotificacoesAvaliadorOrchestrator NovoSut()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_notificador);
        services.AddSingleton(_eventoRepo);
        services.AddSingleton(_tenant);
        services.AddSingleton(_unitOfWork);
        var provider = services.BuildServiceProvider();
        return new NotificacoesAvaliadorOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(), _bypass, _eventoRepo, _rotinaRepo, new RotinaScheduler(),
            NullLogger<NotificacoesAvaliadorOrchestrator>.Instance);
    }

    private EventoNotificacao Pendente(TipoEventoNotificacao tipo, Guid? empresaId = null)
    {
        var evento = EventoNotificacao.Criar(tipo, empresaId ?? Guid.NewGuid(), "{}");
        _eventoRepo.ObterAsync(evento.EmpresaId, evento.Id, Arg.Any<CancellationToken>()).Returns(evento);
        return evento;
    }

    private void ListaPendentes(params EventoNotificacao[] eventos) =>
        _eventoRepo.ListarPendentesParaAvaliarAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<EventoPendente>)eventos.Select(e => new EventoPendente(e.Id, e.EmpresaId)).ToList());

    [Fact]
    public async Task ExecutarRodadaAsync_sem_pendentes_nao_chama_notificador()
    {
        ListaPendentes();

        await NovoSut().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        await _notificador.DidNotReceiveWithAnyArgs().AvaliarEventoAsync(default!, default);
    }

    [Fact]
    public async Task ExecutarRodadaAsync_processa_todos_os_eventos_pendentes_cada_um_com_o_tenant_da_empresa()
    {
        var ev1 = Pendente(TipoEventoNotificacao.ProdutoVencendo);
        var ev2 = Pendente(TipoEventoNotificacao.AssinaturaExpirando);
        ListaPendentes(ev1, ev2);

        await NovoSut().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        await _notificador.Received(1).AvaliarEventoAsync(ev1, Arg.Any<CancellationToken>());
        await _notificador.Received(1).AvaliarEventoAsync(ev2, Arg.Any<CancellationToken>());
        _tenant.Received(1).SetCurrentTenant(ev1.EmpresaId);
        _tenant.Received(1).SetCurrentTenant(ev2.EmpresaId);
    }

    [Fact]
    public async Task ExecutarRodadaAsync_liga_o_bypass_so_na_leitura_da_lista()
    {
        ListaPendentes(Pendente(TipoEventoNotificacao.ProdutoVencendo));

        await NovoSut().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        _bypass.Received(1).Begin();
    }

    [Fact]
    public async Task ExecutarRodadaAsync_evento_com_erro_nao_interrompe_demais_e_termina_Falhado_com_o_motivo()
    {
        // Defesa contra "evento veneno" — falha em um não pode bloquear os outros 199 da página
        var ev1 = Pendente(TipoEventoNotificacao.ProdutoVencendo);
        var ev2 = Pendente(TipoEventoNotificacao.AssinaturaExpirando);
        var ev3 = Pendente(TipoEventoNotificacao.TarefaPendente);
        _notificador.AvaliarEventoAsync(ev2, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("simulado"));
        ListaPendentes(ev1, ev2, ev3);

        var act = async () => await NovoSut().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));
        await act.Should().NotThrowAsync();

        await _notificador.Received(1).AvaliarEventoAsync(ev1, Arg.Any<CancellationToken>());
        await _notificador.Received(1).AvaliarEventoAsync(ev2, Arg.Any<CancellationToken>());
        await _notificador.Received(1).AvaliarEventoAsync(ev3, Arg.Any<CancellationToken>()); // continuou após ev2 falhar
        ev2.Status.Should().Be(StatusEventoNotificacao.Falhado);
        ev2.ErroProcessamento.Should().Contain("simulado");
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ExecutarRodadaAsync_ignora_evento_que_outro_avaliador_ja_fechou()
    {
        var evento = Pendente(TipoEventoNotificacao.ProdutoVencendo);
        evento.MarcarComoProcessado(); // fechado entre a lista e a leitura
        ListaPendentes(evento);

        await NovoSut().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        await _notificador.DidNotReceiveWithAnyArgs().AvaliarEventoAsync(default!, default);
    }
}
