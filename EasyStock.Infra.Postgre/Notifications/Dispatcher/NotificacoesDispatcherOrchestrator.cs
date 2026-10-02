using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Notifications.Dispatcher;

/// <summary>
/// Implementa <see cref="INotificacoesDispatcherOrchestrator"/> (todos os shards) e
/// <see cref="INotificationDispatcher"/> (1 shard) com a mesma lógica subjacente.
/// Usa <see cref="PostgresAdvisoryLock"/> por shard — safe para múltiplas réplicas/processos.
/// Métricas via <see cref="System.Diagnostics.Metrics.Meter"/> (OTel-compatível).
/// </summary>
public sealed class NotificacoesDispatcherOrchestrator(
    IServiceProvider serviceProvider,
    ILogger<NotificacoesDispatcherOrchestrator> logger)
    : INotificacoesDispatcherOrchestrator, INotificationDispatcher
{
    private static readonly Meter NotifMeter = new("EasyStock.Notifications", "1.0");
    private static readonly Counter<long> SentCounter = NotifMeter.CreateCounter<long>("notifications.sent", "notifications", "Total de notificações enviadas com sucesso");
    private static readonly Counter<long> FailedCounter = NotifMeter.CreateCounter<long>("notifications.failed", "notifications", "Total de notificações com falha");
    private static readonly Histogram<long> BatchSizeHistogram = NotifMeter.CreateHistogram<long>("dispatcher.batch.size", "notifications", "Tamanho do batch processado por rodada");
    private static readonly Histogram<double> OutboxLagHistogram = NotifMeter.CreateHistogram<double>("outbox.lag.seconds", "s", "Atraso entre criação e envio da mensagem outbox");
    private static readonly Histogram<double> RunDuration = NotifMeter.CreateHistogram<double>(
        "notifications.dispatcher.run.duration", "ms", "Duração de 1 rodada completa do dispatcher (todos os shards)");

    private static readonly JsonSerializerOptions EnumOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<int> ExecutarRodadaAsync(int shardCount, int batchSize, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var totalProcessadas = 0;
        try
        {
            for (var shard = 0; shard < shardCount; shard++)
            {
                if (ct.IsCancellationRequested) break;
                totalProcessadas += await ProcessarBatchAsync(shard, batchSize, ct);
            }
            return totalProcessadas;
        }
        finally
        {
            sw.Stop();
            RunDuration.Record(sw.Elapsed.TotalMilliseconds,
                new TagList { { "shards", shardCount.ToString() } });
        }
    }

    public async Task<int> ProcessarBatchAsync(int shardKey, int batchSize = 50, CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<EasyStockDbContext>();
        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>();

        var processadas = 0;
        await advisoryLock.TentarExecutarAsync(LockKeys.NotificacoesDispatcherBase + shardKey, async token =>
        {
            var outboxRepo = sp.GetRequiredService<IOutboxNotificacaoRepository>();
            var logRepo = sp.GetRequiredService<ILogEnvioNotificacaoRepository>();
            var eventoRepo = sp.GetRequiredService<IEventoNotificacaoRepository>();
            var templateRepo = sp.GetRequiredService<ITemplateRepository>();
            var rotinaRepo = sp.GetRequiredService<IRotinaRepository>();
            var renderer = sp.GetRequiredService<IRendererTemplate>();
            var canais = sp.GetRequiredService<IEnumerable<ICanalNotificacao>>().ToList();

            var mensagens = await outboxRepo.ListarPendentesParaProcessarAsync(shardKey, batchSize, token);

            foreach (var mensagem in mensagens)
            {
                await ProcessarMensagemAsync(
                    mensagem, canais, outboxRepo, logRepo, eventoRepo, templateRepo, rotinaRepo, renderer, db, token);
            }

            if (mensagens.Count > 0)
            {
                logger.LogInformation(
                    "Shard {Shard}: processadas {Count} mensagens.", shardKey, mensagens.Count);
                BatchSizeHistogram.Record(mensagens.Count, new TagList { { "shard", shardKey.ToString() } });
            }

            processadas = mensagens.Count;
        }, ct);

        return processadas;
    }

    private async Task ProcessarMensagemAsync(
        OutboxMensagemNotificacao mensagem,
        IList<ICanalNotificacao> canais,
        IOutboxNotificacaoRepository outboxRepo,
        ILogEnvioNotificacaoRepository logRepo,
        IEventoNotificacaoRepository eventoRepo,
        ITemplateRepository templateRepo,
        IRotinaRepository rotinaRepo,
        IRendererTemplate renderer,
        EasyStockDbContext db,
        CancellationToken ct)
    {
        var abriuFallback = false;
        var canal = canais.FirstOrDefault(c => c.Canal == mensagem.Canal);
        if (canal is null)
        {
            logger.LogWarning(
                "Nenhum adapter registrado para canal {Canal} outbox={OutboxId}",
                mensagem.Canal, mensagem.Id);
            mensagem.Suprimir($"Canal {mensagem.Canal} sem adapter registrado");
        }
        else
        {
            abriuFallback = await EnviarERegistrarAsync(
                mensagem, canal, logRepo, eventoRepo, templateRepo, rotinaRepo, renderer, db, ct);
        }

        await PurgarPayloadSeForAUltimaAsync(mensagem, abriuFallback, outboxRepo, eventoRepo, ct);

        await outboxRepo.UpdateAsync(mensagem, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Chama o canal e traduz o <see cref="ResultadoEnvio.Desfecho"/> em status do outbox e linha de log (N2):
    /// <c>Enviado</c>; <c>Simulado</c> (log com o provider real, <c>Sucesso = false</c> e erro "simulado");
    /// <c>Indeterminado</c> (terminal, sem reenvio e sem fallback, para não duplicar entre canais); falha permanente
    /// (sem reagendar, com fallback de canal); falha transitória (backoff de 1, 5 e 30 min).
    /// </summary>
    /// <returns>Se abriu uma mensagem de fallback de canal.</returns>
    private async Task<bool> EnviarERegistrarAsync(
        OutboxMensagemNotificacao mensagem,
        ICanalNotificacao canal,
        ILogEnvioNotificacaoRepository logRepo,
        IEventoNotificacaoRepository eventoRepo,
        ITemplateRepository templateRepo,
        IRotinaRepository rotinaRepo,
        IRendererTemplate renderer,
        EasyStockDbContext db,
        CancellationToken ct)
    {
        var mensagemPronta = new MensagemPronta(
            mensagem.Id, mensagem.EmpresaId, mensagem.Destinatario, mensagem.AssuntoRenderizado,
            mensagem.CorpoRenderizado, mensagem.Canal, mensagem.Categoria)
        {
            // S13: template da Meta e parâmetros para o envio fora da janela de 24 h (pendência da S09).
            Metadados = mensagem.LerMetadados()
        };

        var sw = Stopwatch.StartNew();
        var resultado = await canal.EnviarAsync(mensagemPronta, ct);
        sw.Stop();

        var provider = resultado.ProviderUsado ?? mensagem.Canal.ToString();
        var ignoraConsentimento = mensagem.Categoria.IgnoraConsentimento();
        var tags = new TagList { { "canal", mensagem.Canal.ToString() }, { "provider", resultado.ProviderUsado ?? "unknown" } };

        switch (resultado.Desfecho)
        {
            case DesfechoEnvio.Enviado:
            {
                mensagem.MarcarEnviado(provider);
                var lag = (DateTime.UtcNow - mensagem.CriadoEm).TotalSeconds;
                OutboxLagHistogram.Record(lag, new TagList { { "canal", mensagem.Canal.ToString() } });
                SentCounter.Add(1, tags);

                var logSucesso = LogEnvioNotificacao.RegistrarSucesso(
                    mensagem.Id, mensagem.Tentativas + 1, mensagem.Canal, provider,
                    sw.ElapsedMilliseconds,
                    resultado.StatusHttp,
                    resultado.RespostaProviderJson,
                    ignoraConsentimento);

                await logRepo.AddAsync(logSucesso, ct);
                return false;
            }

            case DesfechoEnvio.Simulado:
            {
                // Nada saiu: não conta como enviado, não preenche EnviadoEm e o log não finge sucesso.
                mensagem.MarcarSimulado(provider);

                await logRepo.AddAsync(LogEnvioNotificacao.RegistrarSimulado(
                    mensagem.Id, mensagem.Tentativas + 1, mensagem.Canal, provider,
                    sw.ElapsedMilliseconds, ignoraConsentimento), ct);
                return false;
            }

            case DesfechoEnvio.Indeterminado:
            {
                // Pode ter saído: terminal, sem reenvio e sem fallback de canal (mandar por outro canal duplicaria).
                var erro = resultado.ErroDetalhado ?? "Entrega indeterminada";
                mensagem.MarcarIndeterminado(erro, provider);
                FailedCounter.Add(1, tags);

                await logRepo.AddAsync(
                    LogDeFalha(mensagem, resultado, provider, sw.ElapsedMilliseconds, erro, ignoraConsentimento), ct);
                return false;
            }

            default:
            {
                // FalhaTransitoria (backoff) e FalhaPermanente (Falhado na hora, sem reagendar, com fallback de canal).
                var backoff = mensagem.Tentativas switch
                {
                    0 => TimeSpan.FromMinutes(1),
                    1 => TimeSpan.FromMinutes(5),
                    _ => TimeSpan.FromMinutes(30)
                };

                var erro = resultado.ErroDetalhado ?? "Erro desconhecido";
                mensagem.MarcarFalhaTentativa(erro, backoff, permanente: resultado.Desfecho == DesfechoEnvio.FalhaPermanente);
                FailedCounter.Add(1, tags);

                await logRepo.AddAsync(
                    LogDeFalha(mensagem, resultado, provider, sw.ElapsedMilliseconds, erro, ignoraConsentimento), ct);

                return mensagem.Status == StatusOutbox.Falhado
                    && await TentarFallbackCanalAsync(mensagem, eventoRepo, templateRepo, rotinaRepo, renderer, db, ct);
            }
        }
    }

    private static LogEnvioNotificacao LogDeFalha(
        OutboxMensagemNotificacao mensagem, ResultadoEnvio resultado, string provider, long duracaoMs, string erro,
        bool ignoraConsentimento)
    {
        var log = LogEnvioNotificacao.RegistrarFalha(
            mensagem.Id, mensagem.Tentativas, mensagem.Canal, provider, duracaoMs, erro, resultado.StatusHttp);
        log.BypassConsentimento = ignoraConsentimento;
        return log;
    }

    /// <summary>
    /// Categoria <see cref="CategoriaConteudoNotificacao.Seguranca"/> (N2): a própria mensagem já apagou corpo, assunto e
    /// metadados ao terminar (<see cref="OutboxMensagemNotificacao.PurgarSegredos"/>, no domínio). O payload do evento,
    /// que carrega o mesmo segredo, só sai quando não resta nenhuma outra mensagem aberta do evento e nenhum fallback
    /// foi aberto, porque o fallback de canal relê o payload. É o mesmo commit que fecha a mensagem.
    /// </summary>
    private static async Task PurgarPayloadSeForAUltimaAsync(
        OutboxMensagemNotificacao mensagem,
        bool abriuFallback,
        IOutboxNotificacaoRepository outboxRepo,
        IEventoNotificacaoRepository eventoRepo,
        CancellationToken ct)
    {
        if (mensagem.Categoria != CategoriaConteudoNotificacao.Seguranca) return;
        if (mensagem.Status is StatusOutbox.Pendente or StatusOutbox.EmEnvio) return;
        if (abriuFallback) return;
        if (await outboxRepo.ExisteMensagemAbertaDoEventoAsync(mensagem.EmpresaId, mensagem.EventoId, mensagem.Id, ct)) return;

        var evento = await eventoRepo.GetByIdAsync(mensagem.EventoId, ct);
        evento?.PurgarPayload();
    }

    /// <returns>Se criou a mensagem de fallback (e, por isso, o evento ainda tem uma mensagem aberta).</returns>
    private async Task<bool> TentarFallbackCanalAsync(
        OutboxMensagemNotificacao mensagemOriginal,
        IEventoNotificacaoRepository eventoRepo,
        ITemplateRepository templateRepo,
        IRotinaRepository rotinaRepo,
        IRendererTemplate renderer,
        EasyStockDbContext db,
        CancellationToken ct)
    {
        List<CanalNotificacao> fallbacks;
        try
        {
            fallbacks = JsonSerializer.Deserialize<List<CanalNotificacao>>(
                mensagemOriginal.CanaisFallbackRestantesJson, EnumOptions) ?? [];
        }
        catch
        {
            return false;
        }

        if (fallbacks.Count == 0) return false;

        var proximoCanal = fallbacks[0];
        var fallbackRestantes = fallbacks.Skip(1).ToList();

        var evento = await eventoRepo.GetByIdAsync(mensagemOriginal.EventoId, ct);
        if (evento is null) return false;

        var rotina = (await rotinaRepo.ListarAtivasAsync(evento.Tipo, evento.EmpresaId, ct))
            .FirstOrDefault(r => r.EmpresaId == evento.EmpresaId || r.EmpresaId == null);
        if (rotina is null) return false;

        var template = await templateRepo.GetAtivoAsync(rotina.TemplateCodigo, proximoCanal, evento.EmpresaId, ct)
            ?? await templateRepo.GetAtivoAsync(rotina.TemplateCodigo, proximoCanal, null, ct);
        if (template is null)
        {
            logger.LogWarning(
                "Fallback canal {Canal}: template '{Codigo}' não encontrado — cancelando fallback.",
                proximoCanal, rotina.TemplateCodigo);
            return false;
        }

        var vars = ParsePayload(evento.PayloadJson);
        string assunto, corpo;
        try
        {
            assunto = await renderer.RenderizarAsync(template.AssuntoTemplate, vars, ct);
            var corpoEscapaHtml = proximoCanal is CanalNotificacao.Email or CanalNotificacao.InApp;
            corpo = await renderer.RenderizarAsync(template.CorpoTemplate, vars, corpoEscapaHtml, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao renderizar template para fallback canal {Canal}", proximoCanal);
            return false;
        }

        var novaMsg = OutboxMensagemNotificacao.Criar(
            eventoId: mensagemOriginal.EventoId,
            templateId: template.Id,
            empresaId: mensagemOriginal.EmpresaId,
            canal: proximoCanal,
            destinatario: mensagemOriginal.Destinatario,
            assuntoRenderizado: assunto,
            corpoRenderizado: corpo,
            categoria: mensagemOriginal.Categoria,
            usuarioDestinoId: mensagemOriginal.UsuarioDestinoId,
            canaisFallbackRestantesJson: JsonSerializer.Serialize(fallbackRestantes, EnumOptions));

        await db.NotifOutboxMensagens.AddAsync(novaMsg, ct);

        logger.LogInformation(
            "Fallback criado para canal {Canal} outbox original={OriginalId}",
            proximoCanal, mensagemOriginal.Id);
        return true;
    }

    private static IDictionary<string, object?> ParsePayload(string payloadJson)
    {
        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var doc = JsonDocument.Parse(payloadJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
                vars[prop.Name] = prop.Value.GetString();
        }
        catch { /* silencioso */ }
        return vars;
    }
}
