using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>
/// S07: o Push de <see cref="TipoEventoNotificacao.ConversaEscalada"/> não tem usuário alvo. O destinatário
/// vira <c>empresa:{id}</c>, que o <c>WebPushCanal</c> resolve para todas as subscriptions ativas da empresa.
/// </summary>
public class NotificadorServicePushTests
{
    private readonly IEventoNotificacaoRepository _eventoRepository = Substitute.For<IEventoNotificacaoRepository>();
    private readonly IRotinaRepository _rotinaRepository = Substitute.For<IRotinaRepository>();
    private readonly ITemplateRepository _templateRepository = Substitute.For<ITemplateRepository>();
    private readonly IConsentimentoRepository _consentimentoRepository = Substitute.For<IConsentimentoRepository>();
    private readonly IConfiguracaoCanalRepository _configuracaoRepository = Substitute.For<IConfiguracaoCanalRepository>();
    private readonly IBloqueioNotificacaoRepository _bloqueioRepository = Substitute.For<IBloqueioNotificacaoRepository>();
    private readonly IOutboxNotificacaoRepository _outboxRepository = Substitute.For<IOutboxNotificacaoRepository>();
    private readonly IRendererTemplate _renderer = Substitute.For<IRendererTemplate>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly NotificadorService _service;
    private readonly Guid _empresaId = Guid.NewGuid();

    public NotificadorServicePushTests()
    {
        _service = new NotificadorService(
            _eventoRepository, _rotinaRepository, _templateRepository, _consentimentoRepository,
            _configuracaoRepository, _bloqueioRepository, _outboxRepository, _renderer,
            new ResolvedorCanal(), _unitOfWork, NullLogger<NotificadorService>.Instance);

        _bloqueioRepository.ListarAtivosAsync(Arg.Any<Guid?>(), Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _configuracaoRepository.ListarAsync(null, Arg.Any<CancellationToken>())
            .Returns([ConfiguracaoCanal.Criar(CanalNotificacao.Push, "webpush")]);
        _configuracaoRepository.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([]);
        _renderer.RenderizarAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("assunto");
        _renderer.RenderizarAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns("corpo");

        var rotina = RotinaNotificacao.Criar(
            codigo: "conversa_escalada_global", nome: "Conversa escalada", tipoEvento: TipoEventoNotificacao.ConversaEscalada,
            triggerTipo: TriggerTipoRotina.Evento, templateCodigo: "conversa_escalada_push_v1",
            categoria: CategoriaConteudoNotificacao.Operacional);
        rotina.DefinirFallback("[\"Push\"]", "system");
        _rotinaRepository.ListarAtivasAsync(TipoEventoNotificacao.ConversaEscalada, Arg.Any<CancellationToken>())
            .Returns([rotina]);

        _templateRepository.GetAtivoAsync("conversa_escalada_push_v1", CanalNotificacao.Push, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(TemplateNotificacao.Criar(
                codigo: "conversa_escalada_push_v1", nome: "Conversa escalada — Push", canal: CanalNotificacao.Push,
                tipoEvento: TipoEventoNotificacao.ConversaEscalada,
                assuntoTemplate: "{{ cliente }} precisa de você", corpoTemplate: "{{ motivo }}"));
    }

    [Fact]
    public async Task PushSemUsuarioVaiParaTodasSubscriptionsDaEmpresa()
    {
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.ConversaEscalada, _empresaId,
            """{"conversaId":"c1","cliente":"Maria","motivo":"quer falar com a dona"}""");

        await _service.AvaliarEventoAsync(evento);

        await _outboxRepository.Received(1).AddAsync(
            Arg.Is<OutboxMensagemNotificacao>(o => o.Canal == CanalNotificacao.Push && o.Destinatario == $"empresa:{_empresaId}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushComUsuarioVaiSoParaAsSubscriptionsDele()
    {
        var usuarioId = Guid.NewGuid();
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.ConversaEscalada, _empresaId,
            $$"""{"usuarioId":"{{usuarioId}}","cliente":"Maria","motivo":"x"}""");

        await _service.AvaliarEventoAsync(evento);

        await _outboxRepository.Received(1).AddAsync(
            Arg.Is<OutboxMensagemNotificacao>(o => o.Canal == CanalNotificacao.Push && o.Destinatario == $"usuario:{usuarioId}"),
            Arg.Any<CancellationToken>());
    }
}
