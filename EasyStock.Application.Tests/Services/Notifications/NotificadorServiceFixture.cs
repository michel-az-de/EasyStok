using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>Monta o <see cref="NotificadorService"/> com repositórios falsos para os testes do motor (N5).</summary>
internal sealed class NotificadorServiceFixture
{
    public const string Payload = """{"email":"maria@example.com","telefone":"+5511999990001","nome":"Maria"}""";

    public IEventoNotificacaoRepository Evento { get; } = Substitute.For<IEventoNotificacaoRepository>();
    public IRotinaRepository Rotinas { get; } = Substitute.For<IRotinaRepository>();
    public ITemplateRepository Templates { get; } = Substitute.For<ITemplateRepository>();
    public IConfiguracaoCanalRepository Configuracoes { get; } = Substitute.For<IConfiguracaoCanalRepository>();
    public IBloqueioNotificacaoRepository Bloqueios { get; } = Substitute.For<IBloqueioNotificacaoRepository>();
    public IOutboxNotificacaoRepository Outbox { get; } = Substitute.For<IOutboxNotificacaoRepository>();

    /// <summary>A audiência (N4): por padrão <c>null</c>, isto é, o destinatário sai das chaves do payload.</summary>
    public IResolvedorAudiencia Audiencia { get; } = Substitute.For<IResolvedorAudiencia>();
    public Guid EmpresaId { get; } = Guid.NewGuid();
    public List<OutboxMensagemNotificacao> Gravadas { get; } = [];
    public NotificadorService Service { get; }

    public NotificadorServiceFixture(params CanalNotificacao[] canaisAtivos)
    {
        Service = new NotificadorService(
            Evento, Rotinas, Templates, Substitute.For<IConsentimentoRepository>(), Configuracoes, Bloqueios, Outbox,
            new NotificadorServiceMetadadosTests.RendererTemplateSimples(), new ResolvedorCanal(),
            Substitute.For<IUnitOfWork>(), NullLogger<NotificadorService>.Instance, Audiencia);

        Audiencia.ResolverAsync(
                Arg.Any<RotinaNotificacao>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DestinatarioAudiencia>?)null);
        // Como o banco: o índice único da chave de idempotência barra a segunda linha igual.
        Outbox.ExisteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => Gravadas.Any(g => g.IdempotencyKey == c.Arg<string>()));

        Bloqueios.ListarAtivosAsync(Arg.Any<Guid?>(), Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>()).Returns([]);
        var canais = canaisAtivos.Length > 0 ? canaisAtivos : Enum.GetValues<CanalNotificacao>();
        Configuracoes.ListarAsync(null, Arg.Any<CancellationToken>())
            .Returns(canais.Select(c => ConfiguracaoCanal.Criar(c, "stub")).ToList());
        Configuracoes.ListarAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns([]);
        Outbox.When(o => o.AddAsync(Arg.Any<OutboxMensagemNotificacao>(), Arg.Any<CancellationToken>()))
            .Do(c => Gravadas.Add(c.Arg<OutboxMensagemNotificacao>()));
    }

    public RotinaNotificacao UsarRotina(
        string[] canais,
        CategoriaConteudoNotificacao categoria = CategoriaConteudoNotificacao.Transacional,
        string? parametrosJson = null,
        TipoEventoNotificacao tipo = TipoEventoNotificacao.FaturaVencida)
    {
        var rotina = RotinaNotificacao.Criar(
            "fatura_vencida_global", "Fatura vencida", tipo, TriggerTipoRotina.Evento,
            "fatura_vencida_email_v1", categoria);
        rotina.DefinirFallback("[" + string.Join(",", canais.Select(c => $"\"{c}\"")) + "]", "system");
        if (parametrosJson is not null) rotina.DefinirParametros(parametrosJson, "system");
        Rotinas.ListarAtivasAsync(tipo, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns([rotina]);
        return rotina;
    }

    /// <summary>Template achado só por tipo e canal: o código da rotina não existe no canal (achado 1 da N5).</summary>
    public TemplateNotificacao UsarTemplate(
        CanalNotificacao canal,
        string assunto = "Fatura de {{ nome }}",
        string corpo = "Corpo {{ nome }}",
        string? metadadosJson = null,
        TipoEventoNotificacao tipo = TipoEventoNotificacao.FaturaVencida)
    {
        var template = TemplateNotificacao.Criar($"fatura_vencida_{canal}_v1", "Fatura", canal, tipo, assunto, corpo);
        if (metadadosJson is not null) template.DefinirMetadados(metadadosJson);
        Templates.GetAtivoPorTipoAsync(tipo, canal, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(template);
        return template;
    }

    public EventoNotificacao NovoEvento(string payload = Payload, TipoEventoNotificacao tipo = TipoEventoNotificacao.FaturaVencida) =>
        EventoNotificacao.Criar(tipo, EmpresaId, payload);
}
