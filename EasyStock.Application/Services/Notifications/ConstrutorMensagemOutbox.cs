using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Quem recebe a mensagem (N5): o usuário, quando há, e as variáveis de onde sai o contato de cada canal (e-mail,
/// telefone). A N5 entrega sempre um destinatário, o do payload; a N4 passa a preencher uma lista.
/// </summary>
public sealed record DestinatarioMensagem(Guid? UsuarioId, IDictionary<string, object?> Variaveis);

/// <summary>Por que o canal não gerou mensagem.</summary>
public enum MotivoPulo
{
    SemTemplate,
    SemContato,
    FalhaRenderizacao
}

/// <summary>Mensagem pronta para o outbox, ou o motivo de o canal ter sido pulado.</summary>
public sealed record ResultadoConstrucao(OutboxMensagemNotificacao? Mensagem, MotivoPulo? Pulo = null, string? Detalhe = null);

/// <summary>
/// O único lugar que, dados evento, rotina, canal e destinatário, monta a mensagem do outbox (N5): resolve o template
/// (código da rotina e, sem ele, tipo e canal), renderiza assunto, corpo e metadados (com o prefixo <c>[TESTE]</c>),
/// escolhe o contato do canal, calcula a chave de idempotência e aplica a janela e o <c>enviarApos</c>. Serve ao
/// <see cref="NotificadorService"/> e ao fallback de canal do dispatcher, para os dois não divergirem.
/// </summary>
public sealed class ConstrutorMensagemOutbox(ITemplateRepository templateRepository, IRendererTemplate renderer)
{
    public async Task<ResultadoConstrucao> ConstruirAsync(
        EventoNotificacao evento,
        RotinaNotificacao rotina,
        CanalNotificacao canal,
        DestinatarioMensagem destinatario,
        IReadOnlyList<CanalNotificacao> canaisRestantes,
        DateTime agoraUtc,
        CancellationToken ct = default)
    {
        var template = await ResolverTemplateAsync(rotina, evento, canal, ct);
        if (template is null)
            return new ResultadoConstrucao(null, MotivoPulo.SemTemplate,
                $"Template '{rotina.TemplateCodigo}' (ou do tipo {evento.Tipo}) não encontrado para canal {canal}");

        var vars = destinatario.Variaveis;
        var contato = ResolverContato(vars, canal, evento.EmpresaId);
        if (string.IsNullOrWhiteSpace(contato))
            return new ResultadoConstrucao(null, MotivoPulo.SemContato, $"Destinatário não encontrado para canal {canal}");

        string assunto, corpo;
        string? metadadosJson;
        try
        {
            // Assunto sempre texto puro; corpo HTML em canais que renderizam markup (Email, InApp).
            assunto = await renderer.RenderizarAsync(template.AssuntoTemplate, vars, ct);
            var escapaHtml = canal is CanalNotificacao.Email or CanalNotificacao.InApp;
            corpo = await renderer.RenderizarAsync(template.CorpoTemplate, vars, escapaHtml, ct);
            if (vars.TryGetValue(NotificadorService.TestePayload, out var teste) && teste is true
                && !string.IsNullOrWhiteSpace(assunto))
                assunto = NotificadorService.PrefixoTeste + assunto;
            metadadosJson = await RenderizarMetadadosAsync(template.MetadadosJson, vars, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoConstrucao(null, MotivoPulo.FalhaRenderizacao,
                $"Erro de renderização do template {template.Id} no canal {canal}: {ex.Message}");
        }

        // S13: chave de negócio no payload (ex.: pedido + status) torna o enfileiramento idempotente entre eventos
        // distintos do mesmo fato; o índice único do outbox é a defesa final.
        var chave = vars.TryGetValue(NotificadorService.ChaveIdempotenciaPayload, out var c) && c is string s
                    && !string.IsNullOrWhiteSpace(s)
            ? s
            : null;

        var mensagem = OutboxMensagemNotificacao.Criar(
            eventoId: evento.Id,
            templateId: template.Id,
            empresaId: evento.EmpresaId,
            canal: canal,
            destinatario: contato,
            assuntoRenderizado: assunto,
            corpoRenderizado: corpo,
            categoria: rotina.Categoria,
            usuarioDestinoId: destinatario.UsuarioId,
            canaisFallbackRestantesJson: canaisRestantes.Count > 0 ? CanaisDaRotina.Serializar(canaisRestantes) : "[]",
            metadadosJson: metadadosJson,
            chaveIdempotencia: chave);

        var abertura = JanelaDeEnvio.ProximaAbertura(rotina.JanelaInicio, rotina.JanelaFim, agoraUtc, rotina.Categoria);
        if (abertura is { } instanteAbertura) mensagem.AgendarPara(instanteAbertura);

        if (vars.TryGetValue(NotificadorService.EnviarAposPayload, out var enviarApos) && enviarApos is string instante
            && DateTime.TryParse(instante, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var enviarAposUtc))
            mensagem.AgendarPara(DateTime.SpecifyKind(enviarAposUtc, DateTimeKind.Utc));

        return new ResultadoConstrucao(mensagem);
    }

    /// <summary>Existe template ativo para o canal (pelo código da rotina ou por tipo e canal)?</summary>
    public async Task<bool> TemTemplateAsync(
        RotinaNotificacao rotina, EventoNotificacao evento, CanalNotificacao canal, CancellationToken ct = default) =>
        await ResolverTemplateAsync(rotina, evento, canal, ct) is not null;

    /// <summary>
    /// Variáveis do payload (texto, número e booleano preservados) mais as adicionais, que vencem. Payload malformado
    /// vale conjunto vazio.
    /// </summary>
    public static IDictionary<string, object?> LerVariaveis(
        string payloadJson, IDictionary<string, object?>? adicionais = null)
    {
        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
                vars[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.ToString()
                };
        }
        catch (JsonException) { /* payload malformado */ }
        catch (InvalidOperationException) { /* raiz que não é objeto */ }

        if (adicionais != null)
            foreach (var kv in adicionais)
                vars[kv.Key] = kv.Value;

        return vars;
    }

    /// <summary>Código da rotina (empresa, depois global) e, sem ele no canal, o template do tipo e do canal.</summary>
    private async Task<TemplateNotificacao?> ResolverTemplateAsync(
        RotinaNotificacao rotina, EventoNotificacao evento, CanalNotificacao canal, CancellationToken ct) =>
        await templateRepository.GetAtivoAsync(rotina.TemplateCodigo, canal, evento.EmpresaId, ct)
        ?? await templateRepository.GetAtivoAsync(rotina.TemplateCodigo, canal, null, ct)
        ?? await templateRepository.GetAtivoPorTipoAsync(evento.Tipo, canal, evento.EmpresaId, ct);

    /// <summary>
    /// Renderiza cada valor dos metadados do template com as variáveis do evento (texto puro: vão para a API do
    /// provider, não para HTML). Nulo quando o template não declara metadados.
    /// </summary>
    private async Task<string?> RenderizarMetadadosAsync(
        string? metadadosTemplateJson, IDictionary<string, object?> vars, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(metadadosTemplateJson)) return null;

        var modelos = JsonSerializer.Deserialize<Dictionary<string, string>>(metadadosTemplateJson)
            ?? new Dictionary<string, string>();
        var renderizados = new Dictionary<string, string>(modelos.Count);
        foreach (var (chave, modelo) in modelos)
            renderizados[chave] = await renderer.RenderizarAsync(modelo, vars, ct);

        return JsonSerializer.Serialize(renderizados);
    }

    private static string ResolverContato(IDictionary<string, object?> vars, CanalNotificacao canal, Guid empresaId)
    {
        // Web Push (S07): o usuario do payload recebe em todos os dispositivos dele; sem usuario,
        // todas as subscriptions ativas da empresa (convencao "usuario:"/"empresa:" do WebPushCanal).
        if (canal == CanalNotificacao.Push)
        {
            return vars.TryGetValue("usuarioId", out var uid) && uid is string u && Guid.TryParse(u, out var usuarioId)
                ? $"usuario:{usuarioId}"
                : $"empresa:{empresaId}";
        }

        var chaves = canal switch
        {
            CanalNotificacao.Email => new[] { "email", "emailDestino", "usuarioEmail" },
            CanalNotificacao.Sms => new[] { "telefone", "sms", "celular" },
            CanalNotificacao.WhatsApp => new[] { "telefone", "whatsapp", "celular" },
            CanalNotificacao.InApp => new[] { "usuarioId" },
            _ => Array.Empty<string>()
        };

        foreach (var chave in chaves)
            if (vars.TryGetValue(chave, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                return s;

        return string.Empty;
    }
}
