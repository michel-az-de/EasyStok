using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Campanhas;

/// <summary>Cliente que vai receber a mensagem da campanha, com o telefone já em E.164.</summary>
public sealed record ContatoCampanha(Guid ClienteId, string Nome, string Telefone);

/// <summary>Qual mensagem da campanha sai (S30).</summary>
public enum MensagemCampanha
{
    /// <summary>A mensagem da onda, com o texto da dona.</summary>
    Onda = 1,

    /// <summary>O lembrete do encerramento para quem recebeu e não pediu (RN-43).</summary>
    LembreteEncerramento = 2
}

/// <summary>
/// Envio da campanha recusado antes de chegar ao outbox: kill switch do WhatsApp ou template de
/// notificação ausente. Vira 400 na API e erro no log do job; a campanha fica como estava.
/// </summary>
public sealed class EnvioCampanhaIndisponivelException(string mensagem) : RegraDeDominioVioladaException(mensagem);

/// <summary>
/// Põe as mensagens da campanha no outbox de notificações (S30): um <see cref="OutboxMensagemNotificacao"/>
/// por cliente, canal WhatsApp, categoria <see cref="CategoriaConteudoNotificacao.Marketing"/>, com o
/// texto renderizado e, nos metadados, o template da Meta e os parâmetros para o envio fora da janela
/// de 24 h (S09/S13). O consentimento do cliente final (S38) é conferido por quem chama; aqui fica o
/// kill switch do canal. Só estagia na unidade de trabalho: quem chama faz o único commit (ADR-0030).
/// </summary>
public sealed class EnfileiradorMensagensCampanha(
    ITemplateRepository templateRepository,
    IRendererTemplate renderer,
    IEventoNotificacaoRepository eventoRepository,
    IOutboxNotificacaoRepository outboxRepository,
    IBloqueioNotificacaoRepository bloqueioRepository)
{
    /// <summary>Template de notificação (seed global) que carrega a mensagem da onda.</summary>
    public const string CodigoTemplateOnda = "campanha_marketing_whatsapp_v1";

    /// <summary>Template de notificação (seed global) do lembrete do encerramento.</summary>
    public const string CodigoTemplateLembrete = "campanha_lembrete_whatsapp_v1";

    /// <summary>Template da Meta usado quando a campanha não indica o seu: <c>{{1}}</c> nome, <c>{{2}}</c> texto.</summary>
    public const string TemplateMetaGenerico = "campanha_generica";

    /// <summary>Template da Meta do lembrete: <c>{{1}}</c> nome, <c>{{2}}</c> nome da campanha.</summary>
    public const string TemplateMetaLembrete = "campanha_lembrete";

    public const string IdiomaMeta = "pt_BR";

    private static readonly Regex EspacosRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Estagia uma mensagem por contato, a partir de <paramref name="proximaTentativaEm"/>. Devolve o id
    /// da mensagem do outbox por cliente. Sem contato, não faz nada.
    /// </summary>
    /// <exception cref="EnvioCampanhaIndisponivelException">Kill switch do WhatsApp ativo ou template ausente.</exception>
    public async Task<IReadOnlyDictionary<Guid, Guid>> EnfileirarAsync(
        Campanha campanha,
        MensagemCampanha tipo,
        IReadOnlyList<ContatoCampanha> contatos,
        DateTime proximaTentativaEm,
        DateTime agora,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(campanha);
        ArgumentNullException.ThrowIfNull(contatos);
        if (contatos.Count == 0) return new Dictionary<Guid, Guid>();

        await GarantirCanalLiberadoAsync(campanha.EmpresaId, agora, ct);

        var (codigo, tipoEvento) = tipo == MensagemCampanha.Onda
            ? (CodigoTemplateOnda, TipoEventoNotificacao.CampanhaMarketing)
            : (CodigoTemplateLembrete, TipoEventoNotificacao.CampanhaLembreteEncerramento);
        var template = await templateRepository.GetAtivoAsync(codigo, CanalNotificacao.WhatsApp, campanha.EmpresaId, ct)
            ?? await templateRepository.GetAtivoAsync(codigo, CanalNotificacao.WhatsApp, null, ct)
            ?? throw new EnvioCampanhaIndisponivelException(
                $"Template de notificação '{codigo}' não encontrado para o WhatsApp: a campanha não foi enfileirada.");

        // O evento só dá rastro e a FK do outbox: nasce processado, o avaliador não o pega.
        var evento = EventoNotificacao.Criar(tipoEvento, campanha.EmpresaId,
            JsonSerializer.Serialize(new { campanhaId = campanha.Id, onda = campanha.OndaAtual }), campanha.Id);
        evento.MarcarComoProcessado();
        await eventoRepository.AddAsync(evento, ct);

        var ids = new Dictionary<Guid, Guid>(contatos.Count);
        foreach (var contato in contatos)
        {
            var texto = tipo == MensagemCampanha.Onda
                ? await renderer.RenderizarAsync(campanha.Mensagem, Variaveis(campanha, contato, mensagem: null), ct)
                : string.Empty;
            var variaveis = Variaveis(campanha, contato, texto);
            var corpo = await renderer.RenderizarAsync(template.CorpoTemplate, variaveis, ct);

            var mensagem = OutboxMensagemNotificacao.Criar(
                eventoId: evento.Id,
                templateId: template.Id,
                empresaId: campanha.EmpresaId,
                canal: CanalNotificacao.WhatsApp,
                destinatario: contato.Telefone,
                assuntoRenderizado: string.Empty,
                corpoRenderizado: corpo,
                categoria: CategoriaConteudoNotificacao.Marketing,
                metadadosJson: JsonSerializer.Serialize(Metadados(campanha, tipo, contato, texto)),
                chaveIdempotencia: ChaveIdempotencia(campanha, tipo, contato));
            // Mesmo desenho do adiamento por janela (08/08/2026): o dispatcher só pega a partir daqui.
            mensagem.ProximaTentativaEm = proximaTentativaEm;

            await outboxRepository.AddAsync(mensagem, ct);
            ids[contato.ClienteId] = mensagem.Id;
        }

        return ids;
    }

    private async Task GarantirCanalLiberadoAsync(Guid empresaId, DateTime agora, CancellationToken ct)
    {
        var bloqueios = (await bloqueioRepository.ListarAtivosAsync(null, null, ct))
            .Concat(await bloqueioRepository.ListarAtivosAsync(empresaId, null, ct));
        var bloqueio = bloqueios.FirstOrDefault(b => b.EstaAtivo(agora)
            && (b.EmpresaId is null || b.EmpresaId == empresaId)
            && (b.Canal is null || b.Canal == CanalNotificacao.WhatsApp));
        if (bloqueio is not null)
            throw new EnvioCampanhaIndisponivelException($"Envio pelo WhatsApp bloqueado (kill switch): {bloqueio.Motivo}");
    }

    private static Dictionary<string, object?> Variaveis(Campanha campanha, ContatoCampanha contato, string? mensagem) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["nome"] = contato.Nome,
            ["campanha"] = campanha.Nome,
            ["mensagem"] = mensagem,
        };

    /// <summary>
    /// Template da Meta para fora da janela de 24 h. O genérico recebe nome e texto; o da campanha,
    /// aprovado para ela, só o nome. Parâmetro de template não aceita quebra de linha.
    /// </summary>
    private static Dictionary<string, string> Metadados(
        Campanha campanha, MensagemCampanha tipo, ContatoCampanha contato, string texto)
    {
        var metadados = new Dictionary<string, string> { ["idioma"] = IdiomaMeta, ["param1"] = contato.Nome };
        if (tipo == MensagemCampanha.LembreteEncerramento)
        {
            metadados["template"] = TemplateMetaLembrete;
            metadados["param2"] = campanha.Nome;
        }
        else if (campanha.TemplateMeta is { } proprio)
        {
            metadados["template"] = proprio;
        }
        else
        {
            metadados["template"] = TemplateMetaGenerico;
            metadados["param2"] = EspacosRegex.Replace(texto, " ").Trim();
        }

        return metadados;
    }

    /// <summary>Uma mensagem por cliente na campanha e um lembrete: o índice único do outbox barra a segunda.</summary>
    private static string ChaveIdempotencia(Campanha campanha, MensagemCampanha tipo, ContatoCampanha contato) =>
        tipo == MensagemCampanha.Onda
            ? $"campanha|{campanha.Id:N}|{contato.ClienteId:N}"
            : $"campanha-lembrete|{campanha.Id:N}|{contato.ClienteId:N}";
}
