using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>
/// S13: o template declara os metadados do envio (nome do template da Meta e parâmetros) e o
/// <see cref="NotificadorService"/> os renderiza com as variáveis do evento e persiste no outbox, para o
/// dispatcher entregar ao provider. A chave de idempotência do negócio (<c>chaveIdempotencia</c> no payload)
/// impede a segunda mensagem quando o mesmo fato chega em outro evento.
/// </summary>
public class NotificadorServiceMetadadosTests
{
    private readonly IEventoNotificacaoRepository _eventoRepository = Substitute.For<IEventoNotificacaoRepository>();
    private readonly IRotinaRepository _rotinaRepository = Substitute.For<IRotinaRepository>();
    private readonly ITemplateRepository _templateRepository = Substitute.For<ITemplateRepository>();
    private readonly IConsentimentoRepository _consentimentoRepository = Substitute.For<IConsentimentoRepository>();
    private readonly IConfiguracaoCanalRepository _configuracaoRepository = Substitute.For<IConfiguracaoCanalRepository>();
    private readonly IBloqueioNotificacaoRepository _bloqueioRepository = Substitute.For<IBloqueioNotificacaoRepository>();
    private readonly IOutboxNotificacaoRepository _outboxRepository = Substitute.For<IOutboxNotificacaoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly NotificadorService _service;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly TemplateNotificacao _template;

    public NotificadorServiceMetadadosTests()
    {
        _service = new NotificadorService(
            _eventoRepository, _rotinaRepository, _templateRepository, _consentimentoRepository,
            _configuracaoRepository, _bloqueioRepository, _outboxRepository, new RendererTemplateSimples(),
            new ResolvedorCanal(), _unitOfWork, NullLogger<NotificadorService>.Instance);

        _bloqueioRepository.ListarAtivosAsync(Arg.Any<Guid?>(), Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _configuracaoRepository.ListarAsync(null, Arg.Any<CancellationToken>())
            .Returns([ConfiguracaoCanal.Criar(CanalNotificacao.WhatsApp, "meta")]);
        _configuracaoRepository.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([]);

        var rotina = RotinaNotificacao.Criar(
            codigo: "fatura_vencida_global", nome: "Fatura vencida", tipoEvento: TipoEventoNotificacao.FaturaVencida,
            triggerTipo: TriggerTipoRotina.Evento, templateCodigo: "fatura_vencida_whatsapp_v1",
            categoria: CategoriaConteudoNotificacao.Transacional);
        rotina.DefinirFallback("[\"WhatsApp\"]", "system");
        _rotinaRepository.ListarAtivasAsync(TipoEventoNotificacao.FaturaVencida, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([rotina]);

        _template = TemplateNotificacao.Criar(
            codigo: "fatura_vencida_whatsapp_v1", nome: "Fatura vencida — WhatsApp", canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.FaturaVencida, assuntoTemplate: "", corpoTemplate: "Oi {{ nome }}");
        _templateRepository.GetAtivoAsync("fatura_vencida_whatsapp_v1", CanalNotificacao.WhatsApp, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_template);
    }

    [Fact]
    public async Task WhatsAppComMetadadosRenderizaEPersisteNoOutbox()
    {
        _template.DefinirMetadados("""{"template":"fatura_vencida","param1":"{{ nome }}"}""");
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.FaturaVencida, _empresaId,
            """{"telefone":"+5511999990001","nome":"Maria"}""");

        await _service.AvaliarEventoAsync(evento);

        await _outboxRepository.Received(1).AddAsync(
            Arg.Is<OutboxMensagemNotificacao>(o =>
                o.Canal == CanalNotificacao.WhatsApp
                && o.LerMetadados()!["template"] == "fatura_vencida"
                && o.LerMetadados()!["param1"] == "Maria"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChaveIdempotenciaRepetidaNaoDuplicaOutbox()
    {
        var chaves = new HashSet<string>();
        _outboxRepository.ExisteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => chaves.Contains(c.Arg<string>()));
        _outboxRepository.When(r => r.AddAsync(Arg.Any<OutboxMensagemNotificacao>(), Arg.Any<CancellationToken>()))
            .Do(c => chaves.Add(c.Arg<OutboxMensagemNotificacao>().IdempotencyKey));
        const string payload = """{"telefone":"+5511999990001","nome":"Maria","chaveIdempotencia":"fatura:1|vencida"}""";

        var primeiro = EventoNotificacao.Criar(TipoEventoNotificacao.FaturaVencida, _empresaId, payload);
        var segundo = EventoNotificacao.Criar(TipoEventoNotificacao.FaturaVencida, _empresaId, payload);
        await _service.AvaliarEventoAsync(primeiro);
        await _service.AvaliarEventoAsync(segundo);

        await _outboxRepository.Received(1).AddAsync(Arg.Any<OutboxMensagemNotificacao>(), Arg.Any<CancellationToken>());
        segundo.Status.Should().Be(StatusEventoNotificacao.Processado, "o fato já foi enfileirado; não é falha");
    }

    /// <summary>Substitui <c>{{ chave }}</c> pelas variáveis: basta para os testes, sem Scriban.</summary>
    internal sealed class RendererTemplateSimples : IRendererTemplate
    {
        public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, CancellationToken ct = default)
            => RenderizarAsync(template, variaveis, false, ct);

        public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, bool escaparHtml, CancellationToken ct = default)
        {
            var texto = template;
            foreach (var (chave, valor) in variaveis)
                texto = texto.Replace("{{ " + chave + " }}", valor?.ToString() ?? string.Empty, StringComparison.Ordinal);
            return Task.FromResult(texto);
        }
    }
}
