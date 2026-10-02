using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

public sealed class NotificadorService(
    IEventoNotificacaoRepository eventoRepository,
    IRotinaRepository rotinaRepository,
    ITemplateRepository templateRepository,
    IConsentimentoRepository consentimentoRepository,
    IConfiguracaoCanalRepository configuracaoCanalRepository,
    IBloqueioNotificacaoRepository bloqueioRepository,
    IOutboxNotificacaoRepository outboxRepository,
    IRendererTemplate renderer,
    ResolvedorCanal resolvedorCanal,
    IUnitOfWork unitOfWork,
    ILogger<NotificadorService> logger) : INotificadorService
{
    /// <summary>
    /// Chave do payload com a identidade de negócio do aviso (S13: <c>pedidoId|status</c>). Presente, vira a
    /// <c>IdempotencyKey</c> do outbox no lugar do id do evento.
    /// </summary>
    public const string ChaveIdempotenciaPayload = "chaveIdempotencia";

    /// <summary>
    /// Instante UTC (ISO 8601) a partir do qual a mensagem pode sair (S26). Sem a chave, sai na hora.
    /// </summary>
    public const string EnviarAposPayload = "enviarApos";

    private static readonly JsonSerializerOptions EnumOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task PublicarEventoAsync(
        TipoEventoNotificacao tipo,
        Guid empresaId,
        Guid? usuarioDestinoId,
        string payloadJson,
        IDictionary<string, object?>? varsAdicionais = null,
        CancellationToken ct = default)
    {
        var evento = EventoNotificacao.Criar(tipo, empresaId, payloadJson);
        await eventoRepository.AddAsync(evento, ct);

        await ProcessarEventoInternoAsync(evento, usuarioDestinoId, varsAdicionais, ct);

        await unitOfWork.CommitAsync();
    }

    public async Task EnfileirarEventoAsync(
        TipoEventoNotificacao tipo,
        Guid empresaId,
        string payloadJson,
        Guid? refEntidadeId = null,
        CancellationToken ct = default)
    {
        // ADR-0030: só estagia o evento Pendente na UoW atual (sem ProcessarEventoInternoAsync,
        // sem CommitAsync). O caller commita junto com a mutação de negócio (atômico). O
        // Avaliador processa fora de banda — nada aguardado/falível após o commit do negócio.
        var evento = EventoNotificacao.Criar(tipo, empresaId, payloadJson, refEntidadeId);
        await eventoRepository.AddAsync(evento, ct);
    }

    public async Task AvaliarEventoAsync(EventoNotificacao evento, CancellationToken ct = default)
    {
        Guid? usuarioDestinoId = null;

        try
        {
            var doc = JsonDocument.Parse(evento.PayloadJson);
            if (doc.RootElement.TryGetProperty("usuarioId", out var uid) &&
                Guid.TryParse(uid.GetString(), out var parsedId))
            {
                usuarioDestinoId = parsedId;
            }
        }
        catch (JsonException) { /* payload inválido — continua sem usuário específico */ }

        try
        {
            await ProcessarEventoInternoAsync(evento, usuarioDestinoId, varsAdicionais: null, ct);
            await unitOfWork.CommitAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — não marca como falhado, evento volta a ser pendente na próxima rodada.
            throw;
        }
        catch (Exception ex)
        {
            // Defesa contra "evento veneno": qualquer exceção não tratada vira Falhado
            // pra evitar loop infinito com starvation dos demais (orderBy OcorridoEm + Take 200).
            logger.LogError(ex,
                "Falha não recuperável ao avaliar evento {EventoId} (Tipo={Tipo}) — marcado como Falhado",
                evento.Id, evento.Tipo);
            try
            {
                evento.MarcarComoFalhado($"Erro não tratado: {ex.GetType().Name}: {ex.Message}");
                await eventoRepository.UpdateAsync(evento, ct);
                await unitOfWork.CommitAsync();
            }
            catch (Exception saveEx)
            {
                // Se nem conseguimos persistir o status Falhado, propaga o erro original.
                logger.LogError(saveEx,
                    "Erro adicional ao tentar marcar evento {EventoId} como Falhado",
                    evento.Id);
                throw;
            }
        }
    }

    private async Task ProcessarEventoInternoAsync(
        EventoNotificacao evento,
        Guid? usuarioDestinoId,
        IDictionary<string, object?>? varsAdicionais,
        CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        var bloqueiosGlobais = await bloqueioRepository.ListarAtivosAsync(null, null, ct);
        var bloqueiosEmpresa = await bloqueioRepository.ListarAtivosAsync(evento.EmpresaId, null, ct);
        var todosBloqueios = bloqueiosGlobais.Concat(bloqueiosEmpresa).ToList();

        if (todosBloqueios.Any(b => b.EstaAtivo(agora) && b.Canal == null && b.EmpresaId == null))
        {
            logger.LogInformation("Kill switch global ativo — evento {EventoId} suprimido", evento.Id);
            evento.MarcarComoProcessado();
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        var rotina = (await rotinaRepository.ListarAtivasAsync(evento.Tipo, evento.EmpresaId, ct))
            .FirstOrDefault(r => r.EmpresaId == evento.EmpresaId || r.EmpresaId == null);

        if (rotina is null)
        {
            logger.LogDebug(
                "Nenhuma rotina ativa para TipoEvento={TipoEvento} EmpresaId={EmpresaId}",
                evento.Tipo, evento.EmpresaId);
            evento.MarcarComoProcessado();
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        var canaisPreferidos = ParseCanais(rotina.CanaisOrdemFallbackJson);
        if (canaisPreferidos.Count == 0)
        {
            evento.MarcarComoProcessado();
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        var consentimentos = usuarioDestinoId.HasValue
            ? await consentimentoRepository.ListarPorUsuarioAsync(usuarioDestinoId.Value, ct)
            : (IReadOnlyList<ConsentimentoNotificacao>)[];

        var configuracoes = await configuracaoCanalRepository.ListarAsync(evento.EmpresaId, ct);
        var configuracoesFallback = await configuracaoCanalRepository.ListarAsync(null, ct);
        var todasConfiguracoes = configuracoes
            .Concat(configuracoesFallback.Where(gf => configuracoes.All(ef => ef.Canal != gf.Canal)))
            .ToList();

        var canaisPermitidos = resolvedorCanal.ResolverCanaisPermitidos(
            rotina.Categoria,
            canaisPreferidos,
            consentimentos,
            todasConfiguracoes,
            todosBloqueios,
            agora);

        if (canaisPermitidos.Count == 0)
        {
            logger.LogInformation(
                "Nenhum canal permitido para evento {EventoId} usuário {UsuarioId}",
                evento.Id, usuarioDestinoId);
            evento.MarcarComoProcessado();
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        var canalPrimario = canaisPermitidos[0];
        var canaisFallback = canaisPermitidos.Skip(1).ToList();

        var template = await ResolverTemplateAsync(rotina.TemplateCodigo, canalPrimario, evento.EmpresaId, ct);
        if (template is null)
        {
            logger.LogWarning(
                "Template não encontrado: codigo={Codigo} canal={Canal} empresa={EmpresaId}",
                rotina.TemplateCodigo, canalPrimario, evento.EmpresaId);
            evento.MarcarComoFalhado($"Template '{rotina.TemplateCodigo}' não encontrado para canal {canalPrimario}");
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        var vars = ConstruirVariaveis(evento.PayloadJson, varsAdicionais);

        string assunto;
        string corpo;

        try
        {
            // Assunto sempre texto puro; corpo HTML em canais que renderizam markup (Email, InApp).
            assunto = await renderer.RenderizarAsync(template.AssuntoTemplate, vars, ct);
            var corpoEscapaHtml = canalPrimario is CanalNotificacao.Email or CanalNotificacao.InApp;
            corpo = await renderer.RenderizarAsync(template.CorpoTemplate, vars, corpoEscapaHtml, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Erro ao renderizar template {TemplateId} para evento {EventoId}",
                template.Id, evento.Id);
            evento.MarcarComoFalhado($"Erro de renderização: {ex.Message}");
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        var destinatario = ResolverDestinatario(vars, canalPrimario, evento.EmpresaId);
        if (string.IsNullOrWhiteSpace(destinatario))
        {
            logger.LogWarning(
                "Destinatário não resolvido para evento {EventoId} canal {Canal}",
                evento.Id, canalPrimario);
            evento.MarcarComoFalhado($"Destinatário não encontrado para canal {canalPrimario}");
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        // S13: chave de negócio no payload (ex.: pedido + status) torna o enfileiramento idempotente entre
        // eventos distintos do mesmo fato; o índice único do outbox é a defesa final.
        var chaveIdempotencia = vars.TryGetValue(ChaveIdempotenciaPayload, out var chave) && chave is string c && !string.IsNullOrWhiteSpace(c)
            ? c
            : null;
        if (chaveIdempotencia is not null
            && await outboxRepository.ExisteAsync(OutboxMensagemNotificacao.ComputarIdempotencyKey(chaveIdempotencia, canalPrimario), ct))
        {
            logger.LogInformation(
                "Evento {EventoId} repete um fato já enfileirado (Tipo={Tipo} canal {Canal}) — sem nova mensagem",
                evento.Id, evento.Tipo, canalPrimario);
            evento.MarcarComoProcessado();
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        string? metadadosJson;
        try
        {
            metadadosJson = await RenderizarMetadadosAsync(template.MetadadosJson, vars, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Erro ao renderizar metadados do template {TemplateId} para evento {EventoId}",
                template.Id, evento.Id);
            evento.MarcarComoFalhado($"Erro de renderização dos metadados: {ex.Message}");
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        var canaisFallbackJson = canaisFallback.Count > 0
            ? JsonSerializer.Serialize(canaisFallback, EnumOptions)
            : "[]";

        var outbox = OutboxMensagemNotificacao.Criar(
            eventoId: evento.Id,
            templateId: template.Id,
            empresaId: evento.EmpresaId,
            canal: canalPrimario,
            destinatario: destinatario,
            assuntoRenderizado: assunto,
            corpoRenderizado: corpo,
            categoria: rotina.Categoria,
            usuarioDestinoId: usuarioDestinoId,
            canaisFallbackRestantesJson: canaisFallbackJson,
            metadadosJson: metadadosJson,
            chaveIdempotencia: chaveIdempotencia);

        if (vars.TryGetValue(EnviarAposPayload, out var enviarApos) && enviarApos is string instante
            && DateTime.TryParse(instante, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var enviarAposUtc))
            outbox.AgendarPara(DateTime.SpecifyKind(enviarAposUtc, DateTimeKind.Utc));

        await outboxRepository.AddAsync(outbox, ct);

        evento.MarcarComoProcessado();
        await eventoRepository.UpdateAsync(evento, ct);
    }

    /// <summary>
    /// Renderiza cada valor dos metadados do template com as variáveis do evento (texto puro: vão para a API
    /// do provider, não para HTML). Nulo quando o template não declara metadados.
    /// </summary>
    private async Task<string?> RenderizarMetadadosAsync(
        string? metadadosTemplateJson,
        IDictionary<string, object?> vars,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(metadadosTemplateJson)) return null;

        var modelos = JsonSerializer.Deserialize<Dictionary<string, string>>(metadadosTemplateJson)
            ?? new Dictionary<string, string>();
        var renderizados = new Dictionary<string, string>(modelos.Count);
        foreach (var (chave, modelo) in modelos)
            renderizados[chave] = await renderer.RenderizarAsync(modelo, vars, ct);

        return JsonSerializer.Serialize(renderizados);
    }

    private async Task<TemplateNotificacao?> ResolverTemplateAsync(
        string codigo,
        CanalNotificacao canal,
        Guid empresaId,
        CancellationToken ct)
    {
        return await templateRepository.GetAtivoAsync(codigo, canal, empresaId, ct)
            ?? await templateRepository.GetAtivoAsync(codigo, canal, null, ct);
    }

    private static IReadOnlyList<CanalNotificacao> ParseCanais(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CanalNotificacao>>(json, EnumOptions)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IDictionary<string, object?> ConstruirVariaveis(
        string payloadJson,
        IDictionary<string, object?>? varsAdicionais)
    {
        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var doc = JsonDocument.Parse(payloadJson);
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

        if (varsAdicionais != null)
            foreach (var kv in varsAdicionais)
                vars[kv.Key] = kv.Value;

        return vars;
    }

    private static string ResolverDestinatario(
        IDictionary<string, object?> vars,
        CanalNotificacao canal,
        Guid empresaId)
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
