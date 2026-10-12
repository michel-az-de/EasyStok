using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N13: evento de teste (<c>teste: true</c> no payload) sai com o assunto prefixado por <c>[TESTE]</c>.</summary>
public class NotificadorServiceTesteTests
{
    private readonly IEventoNotificacaoRepository _eventoRepository = Substitute.For<IEventoNotificacaoRepository>();
    private readonly IRotinaRepository _rotinaRepository = Substitute.For<IRotinaRepository>();
    private readonly ITemplateRepository _templateRepository = Substitute.For<ITemplateRepository>();
    private readonly IConfiguracaoCanalRepository _configuracaoRepository = Substitute.For<IConfiguracaoCanalRepository>();
    private readonly IBloqueioNotificacaoRepository _bloqueioRepository = Substitute.For<IBloqueioNotificacaoRepository>();
    private readonly IOutboxNotificacaoRepository _outboxRepository = Substitute.For<IOutboxNotificacaoRepository>();
    private readonly NotificadorService _service;
    private readonly Guid _empresaId = Guid.NewGuid();

    public NotificadorServiceTesteTests()
    {
        _service = new NotificadorService(
            _eventoRepository, _rotinaRepository, _templateRepository, Substitute.For<IConsentimentoRepository>(),
            _configuracaoRepository, _bloqueioRepository, _outboxRepository,
            new NotificadorServiceMetadadosTests.RendererTemplateSimples(),
            new ResolvedorCanal(), Substitute.For<IUnitOfWork>(), NullLogger<NotificadorService>.Instance);

        _bloqueioRepository.ListarAtivosAsync(Arg.Any<Guid?>(), Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _configuracaoRepository.ListarAsync(null, Arg.Any<CancellationToken>())
            .Returns([ConfiguracaoCanal.Criar(CanalNotificacao.Email, "stub")]);
        _configuracaoRepository.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([]);

        var rotina = RotinaNotificacao.Criar(
            codigo: "incidente_sistema_global", nome: "Incidente", tipoEvento: TipoEventoNotificacao.IncidenteSistema,
            triggerTipo: TriggerTipoRotina.Evento, templateCodigo: "incidente_sistema_email_v1",
            categoria: CategoriaConteudoNotificacao.Operacional);
        rotina.DefinirFallback("[\"Email\"]", "system");
        _rotinaRepository.ListarAtivasAsync(TipoEventoNotificacao.IncidenteSistema, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([rotina]);

        var template = TemplateNotificacao.Criar(
            "incidente_sistema_email_v1", "Incidente", CanalNotificacao.Email, TipoEventoNotificacao.IncidenteSistema,
            "EasyStok: {{ componente }} instável", "Corpo {{ componente }}");
        _templateRepository.GetAtivoAsync("incidente_sistema_email_v1", CanalNotificacao.Email, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(template);
    }

    [Fact]
    public async Task AssuntoDeEventoDeTesteGanhaPrefixo()
    {
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.IncidenteSistema, _empresaId,
            """{"email":"admin@example.com","componente":"Envio de e-mail","teste":true}""");

        await _service.AvaliarEventoAsync(evento);

        await _outboxRepository.Received(1).AddAsync(
            Arg.Is<OutboxMensagemNotificacao>(o => o.AssuntoRenderizado == "[TESTE] EasyStok: Envio de e-mail instável"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("""{"email":"admin@example.com","componente":"Caixa"}""")]
    [InlineData("""{"email":"admin@example.com","componente":"Caixa","teste":false}""")]
    public async Task EventoRealNaoGanhaPrefixo(string payload)
    {
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.IncidenteSistema, _empresaId, payload);

        await _service.AvaliarEventoAsync(evento);

        await _outboxRepository.Received(1).AddAsync(
            Arg.Is<OutboxMensagemNotificacao>(o => o.AssuntoRenderizado == "EasyStok: Caixa instável"),
            Arg.Any<CancellationToken>());
    }
}
