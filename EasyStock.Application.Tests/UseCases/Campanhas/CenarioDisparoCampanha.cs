using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Campanhas;

/// <summary>
/// Cenário do disparo (S30): repositório e queries substituídos, outbox que guarda o que foi estagiado
/// e relógio controlado. Os destinatários ficam em memória e o repositório os devolve por status.
/// </summary>
internal sealed class CenarioDisparoCampanha
{
    public static readonly DateTimeOffset Inicio = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    public static DateTime Disparo => Inicio.UtcDateTime.AddHours(1);

    public Guid EmpresaId { get; } = Guid.NewGuid();
    public ICampanhaRepository Repo { get; } = Substitute.For<ICampanhaRepository>();
    public ICampanhaPublicoQueries Queries { get; } = Substitute.For<ICampanhaPublicoQueries>();
    public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
    public ITemplateRepository Templates { get; } = Substitute.For<ITemplateRepository>();
    public IEventoNotificacaoRepository Eventos { get; } = Substitute.For<IEventoNotificacaoRepository>();
    public IOutboxNotificacaoRepository Outbox { get; } = Substitute.For<IOutboxNotificacaoRepository>();
    public IBloqueioNotificacaoRepository Bloqueios { get; } = Substitute.For<IBloqueioNotificacaoRepository>();
    public FakeTimeProvider Relogio { get; } = new(Inicio);

    public List<CampanhaDestinatario> Destinatarios { get; } = [];
    public List<CandidatoPublicoCampanha> Candidatos { get; } = [];
    public List<OutboxMensagemNotificacao> Mensagens { get; } = [];
    public List<EventoNotificacao> EventosCriados { get; } = [];

    public CenarioDisparoCampanha()
    {
        Outbox.AddAsync(Arg.Do<OutboxMensagemNotificacao>(Mensagens.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        Eventos.AddAsync(Arg.Do<EventoNotificacao>(EventosCriados.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        Bloqueios.ListarAtivosAsync(Arg.Any<Guid?>(), Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BloqueioNotificacao>());

        Templates.GetAtivoAsync(EnfileiradorMensagensCampanha.CodigoTemplateOnda, CanalNotificacao.WhatsApp, null, Arg.Any<CancellationToken>())
            .Returns(Template(EnfileiradorMensagensCampanha.CodigoTemplateOnda, TipoEventoNotificacao.CampanhaMarketing,
                "{{ mensagem }}\n\nPara não receber mais, responda SAIR."));
        Templates.GetAtivoAsync(EnfileiradorMensagensCampanha.CodigoTemplateLembrete, CanalNotificacao.WhatsApp, null, Arg.Any<CancellationToken>())
            .Returns(Template(EnfileiradorMensagensCampanha.CodigoTemplateLembrete, TipoEventoNotificacao.CampanhaLembreteEncerramento,
                "{{ nome }}, a campanha {{ campanha }} está terminando."));

        Repo.ListarPendentesAsync(EmpresaId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => Destinatarios.Where(d => d.CampanhaId == c.ArgAt<Guid>(1) && d.Status == StatusCampanhaDestinatario.Pendente).ToList());
        Repo.ListarEnviadosAsync(EmpresaId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => Destinatarios.Where(d => d.CampanhaId == c.ArgAt<Guid>(1) && d.Status == StatusCampanhaDestinatario.Enviado).ToList());
        Repo.ListarEnfileiradosAsync(EmpresaId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => Destinatarios
                .Where(d => d.CampanhaId == c.ArgAt<Guid>(1) && d.Status == StatusCampanhaDestinatario.Enfileirado)
                .Select(d => new EnvioDestinatarioCampanha(d, Mensagens.SingleOrDefault(m => m.Id == d.OutboxMensagemId)))
                .ToList());
        Queries.ListarCandidatosAsync(EmpresaId, Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Candidatos.ToList());
    }

    public Campanha Campanha(int? tamanhoOnda = null, Guid? itemId = null, DateTime? encerramentoEm = null,
        bool lembrete = false, string? templateMeta = null, string? imagemUrl = null)
    {
        var filtro = new FiltroCampanha(true, [], [], itemId, itemId is null ? null : 90);
        var campanha = Domain.Entities.Campanhas.Campanha.Criar(EmpresaId, Guid.NewGuid(),
            new DadosCampanha("Bolo de fubá", "Oi {{nome}}, saiu bolo de fubá!", imagemUrl, templateMeta, filtro, [],
                encerramentoEm, lembrete, tamanhoOnda),
            Inicio.UtcDateTime);
        campanha.Agendar(Disparo, Inicio.UtcDateTime);
        Repo.ObterAsync(EmpresaId, campanha.Id, Arg.Any<CancellationToken>()).Returns(campanha);
        return campanha;
    }

    /// <summary>Cliente pendente na campanha e candidato do público; <paramref name="comprouEm"/> dá a prioridade.</summary>
    public CandidatoPublicoCampanha Pendente(Campanha campanha, string nome, DateTime? comprouEm = null,
        bool consentiu = true, string? telefone = "(11) 99757-3992")
    {
        var cliente = new CandidatoPublicoCampanha(Guid.NewGuid(), nome, telefone is not null, false, consentiu, [],
            comprouEm, null, telefone);
        Candidatos.Add(cliente);
        Destinatarios.Add(CampanhaDestinatario.Criar(campanha, cliente.ClienteId));
        return cliente;
    }

    public CampanhaDestinatario Destinatario(CandidatoPublicoCampanha cliente) =>
        Destinatarios.Single(d => d.ClienteId == cliente.ClienteId);

    /// <summary>O dispatcher mandou (ou desistiu de) todas as mensagens ainda pendentes no outbox.</summary>
    public void OutboxProcessou(bool sucesso = true)
    {
        foreach (var mensagem in Mensagens.Where(m => m.Status == StatusOutbox.Pendente))
        {
            if (sucesso) mensagem.MarcarEnviado("whatsapp:meta");
            else mensagem.MarcarFalhaTentativa("template inexistente", TimeSpan.Zero, permanente: true);
        }
    }

    public EnfileiradorMensagensCampanha Enfileirador() =>
        new(Templates, new RendererChaves(), Eventos, Outbox, Bloqueios);

    public DispararOndaCampanhaUseCase Disparar() => new(Repo, Queries, Enfileirador(), Uow, Relogio);

    public ProcessarCampanhaUseCase Job() => new(Repo, Queries, Disparar(), Enfileirador(), Uow, Relogio);

    private static TemplateNotificacao Template(string codigo, TipoEventoNotificacao tipo, string corpo) =>
        TemplateNotificacao.Criar(codigo, codigo, CanalNotificacao.WhatsApp, tipo, "", corpo);

    /// <summary>Troca <c>{{chave}}</c> e <c>{{ chave }}</c> pelas variáveis: basta para os testes, sem Scriban.</summary>
    private sealed class RendererChaves : IRendererTemplate
    {
        public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, CancellationToken ct = default) =>
            RenderizarAsync(template, variaveis, false, ct);

        public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, bool htmlEscape, CancellationToken ct = default) =>
            Task.FromResult(Regex.Replace(template, @"\{\{\s*(\w+)\s*\}\}",
                m => variaveis.TryGetValue(m.Groups[1].Value, out var v) ? v?.ToString() ?? string.Empty : string.Empty));
    }
}
