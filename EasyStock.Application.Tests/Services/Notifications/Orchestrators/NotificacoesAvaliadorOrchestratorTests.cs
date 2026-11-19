using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Atendimento;
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
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IRowLevelSecurityBypass _bypass = Substitute.For<IRowLevelSecurityBypass>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private NotificacoesAvaliadorOrchestrator NovoSut(Action<IServiceCollection>? registrar = null)
    {
        var services = new ServiceCollection();
        registrar?.Invoke(services);
        services.AddSingleton(_notificador);
        services.AddSingleton(_eventoRepo);
        services.AddSingleton(_tenant);
        services.AddSingleton(_unitOfWork);
        var provider = services.BuildServiceProvider();
        return new NotificacoesAvaliadorOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(), _bypass, _eventoRepo, _unitOfWork,
            new PoliticaValidadeNotificacao(),
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
    public void NaoTemMaisRamoCron()
    {
        // O ramo cron era um stub que só logava "matched" (N12): o agendamento é horário diário no coletor, sem cron.
        var parametros = typeof(NotificacoesAvaliadorOrchestrator).GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType).ToList();

        parametros.Should().NotContain(t => t.Name == "RotinaScheduler");
        parametros.Should().NotContain(typeof(IRotinaRepository), "a lista de rotinas só servia ao ramo cron");
        typeof(INotificacoesAvaliadorOrchestrator).Assembly.GetReferencedAssemblies()
            .Should().NotContain(a => a.Name == "Cronos");
    }

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

    /// <summary>
    /// #2875: o aviso ao cliente que expira sem ser avaliado vai à conversa como não entregue, no escopo da empresa e
    /// com commit próprio, depois do commit da expiração.
    /// </summary>
    [Fact]
    public async Task ExecutarRodadaAsync_leva_o_aviso_ao_cliente_expirado_a_conversa_no_escopo_da_empresa()
    {
        const string telefone = "5511987654321";
        var empresa = Guid.NewGuid();
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.ComandaAtualizada, empresa,
            $$"""{"telefone":"+{{telefone}}","nome":"Maria","numero":"12"}""", Guid.NewGuid());
        evento.MarcarComoExpirado("Expirado: passou do prazo de 120 min sem ser avaliado");
        _eventoRepo.ObterAsync(empresa, evento.Id, Arg.Any<CancellationToken>()).Returns(evento);
        _eventoRepo.ExpirarPendentesAsync(Arg.Any<PoliticaValidadeNotificacao>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new NotificacaoExpirada(evento.Id, empresa, evento.Tipo),
                      new NotificacaoExpirada(Guid.NewGuid(), empresa, TipoEventoNotificacao.ProdutoVencendo)]);
        ListaPendentes();

        var rotina = RotinaNotificacao.Criar(
            codigo: "comanda_atualizada_global", nome: "Comanda atualizada", tipoEvento: TipoEventoNotificacao.ComandaAtualizada,
            triggerTipo: TriggerTipoRotina.Evento, templateCodigo: "comanda_atualizada_whatsapp_v1",
            categoria: CategoriaConteudoNotificacao.Transacional);
        rotina.DefinirFallback("[\"WhatsApp\"]", "system");
        var rotinas = Substitute.For<IRotinaRepository>();
        rotinas.ListarAtivasAsync(TipoEventoNotificacao.ComandaAtualizada, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([rotina]);
        var templates = Substitute.For<ITemplateRepository>();
        templates.GetAtivoAsync("comanda_atualizada_whatsapp_v1", CanalNotificacao.WhatsApp, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(TemplateNotificacao.Criar(
                codigo: "comanda_atualizada_whatsapp_v1", nome: "Comanda atualizada", canal: CanalNotificacao.WhatsApp,
                tipoEvento: TipoEventoNotificacao.ComandaAtualizada, assuntoTemplate: "",
                corpoTemplate: "{{ nome }}, comanda nº {{ numero }}."));
        var conversas = Substitute.For<IConversaRepository>();
        conversas.ObterAbertaPorContatoAsync(empresa, CanalConversa.WhatsApp, telefone, Arg.Any<CancellationToken>())
            .Returns(Conversa.Abrir(empresa, telefone, DateTime.UtcNow.AddDays(-1), "Maria"));

        await NovoSut(services =>
        {
            services.AddSingleton(rotinas);
            services.AddSingleton(templates);
            services.AddSingleton(conversas);
            services.AddSingleton<IRendererTemplate>(new NotificadorServiceMetadadosTests.RendererTemplateSimples());
            services.AddSingleton(new PoliticaValidadeNotificacao());
            services.AddScoped<AvisoNaoEntregueNaConversa>();
            services.AddScoped<AvisoEventoExpiradoNaConversa>();
        }).ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        await conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Status == StatusMensagem.Falhou && m.Texto == "Maria, comanda nº 12."),
            Arg.Any<CancellationToken>());
        _tenant.Received().SetCurrentTenant(empresa);
        await _unitOfWork.Received(2).CommitAsync(); // a expiração e o registro na conversa
    }
}
