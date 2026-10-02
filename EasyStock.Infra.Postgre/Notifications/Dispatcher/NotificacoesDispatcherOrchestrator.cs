using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Notifications.Dispatcher;

/// <summary>
/// Implementa <see cref="INotificacoesDispatcherOrchestrator"/> e <see cref="INotificationDispatcher"/> com a mesma
/// rodada. Padrão da S39 (<c>MensagensProgramadasBackgroundService</c>):
/// <list type="number">
/// <item>escopo A, com o bypass de RLS ligado pela porta <see cref="IRowLevelSecurityBypass"/> ANTES de qualquer
/// conexão (o interceptor lê a flag na abertura), numa transação curta: reserva as <c>Pendente</c> elegíveis com
/// <c>FOR UPDATE SKIP LOCKED</c>, passa-as para <c>EmEnvio</c> com lease e devolve só <c>(Id, EmpresaId, Canal)</c>;</item>
/// <item>por mensagem, um escopo de DI novo com o tenant da empresa fixado antes da primeira conexão e um
/// <c>try/catch</c> próprio: a falha de uma mensagem vira <c>Falhado</c> com motivo (ou <c>Indeterminado</c>) e nunca
/// para o lote.</item>
/// </list>
/// Sem advisory lock: <c>SKIP LOCKED</c> mais o lease dão a exclusão entre réplicas. <c>ShardKey</c> e o parâmetro de
/// shards ficam só por compatibilidade e são ignorados. Métricas via <see cref="Meter"/> (OTel-compatível).
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
        "notifications.dispatcher.run.duration", "ms", "Duração de 1 rodada completa do dispatcher");

    /// <summary>Teto de lotes por rodada: o loop do host precisa voltar a respirar (cancelamento, heartbeat).</summary>
    private const int MaxLotesPorRodada = 20;

    /// <summary>Teto de mensagens expiradas por grupo de prazo em cada reserva; a rodada seguinte continua o resto.</summary>
    private const int LimiteExpiracaoPorPrazo = 500;

    /// <summary>Teto de leases vencidos reclamados em cada reserva.</summary>
    private const int LimiteLeasesPorReserva = 500;

    private static readonly JsonSerializerOptions EnumOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Mensagem reservada pelo claim: o mínimo para abrir o escopo da empresa e reler a linha.</summary>
    private sealed record MensagemReservada(Guid EmpresaId, Guid Id, CanalNotificacao Canal);

    /// <summary>O que o escopo da mensagem já fez, para a falha saber se o canal chegou a ser chamado.</summary>
    private sealed class EstadoDoItem
    {
        public bool CanalChamado { get; set; }
    }

    /// <summary>O parâmetro de shards é ignorado (N1): o claim não depende de <c>ShardKey</c>.</summary>
    public Task<int> ExecutarRodadaAsync(int shardCount, int batchSize, CancellationToken ct = default) =>
        RodadaAsync(batchSize, ct);

    /// <summary>Compatibilidade com o gatilho HTTP (<c>?shard=</c>): o shard é ignorado e roda a rodada completa.</summary>
    public Task<int> ProcessarBatchAsync(int shardKey, int batchSize = 50, CancellationToken ct = default) =>
        RodadaAsync(batchSize, ct);

    private async Task<int> RodadaAsync(int batchSize, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var total = 0;
        try
        {
            for (var lote = 0; lote < MaxLotesPorRodada && !ct.IsCancellationRequested; lote++)
            {
                var reservadas = await ReservarAsync(batchSize, ct);
                if (reservadas.Count == 0) break;

                foreach (var reservada in reservadas)
                {
                    ct.ThrowIfCancellationRequested();
                    await ProcessarItemAsync(reservada, ct);
                }

                total += reservadas.Count;
                BatchSizeHistogram.Record(reservadas.Count);
                logger.LogInformation("Dispatcher: processadas {Count} mensagens.", reservadas.Count);
                if (reservadas.Count < batchSize) break;
            }

            return total;
        }
        finally
        {
            sw.Stop();
            RunDuration.Record(sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Passo 1 (cross-tenant): o bypass entra pela porta antes de qualquer conexão e tudo roda numa transação curta,
    /// sem retentativa (o bloco não é idempotente): (a) expira o outbox <c>Pendente</c> além do prazo do tipo, para o
    /// backlog velho nunca ficar elegível; (b) reclama o <c>EmEnvio</c> com lease vencido (e-mail volta a <c>Pendente</c>,
    /// WhatsApp e SMS viram <c>Indeterminado</c>); (c) reserva as elegíveis. Devolve só ids: as linhas são relidas no
    /// escopo da empresa.
    /// </summary>
    private async Task<IReadOnlyList<MensagemReservada>> ReservarAsync(int batchSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        using var _ = sp.GetRequiredService<IRowLevelSecurityBypass>().Begin();
        var outboxRepo = sp.GetRequiredService<IOutboxNotificacaoRepository>();
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var politica = sp.GetRequiredService<PoliticaValidadeNotificacao>();

        return await unitOfWork.ExecuteInTransactionSemRetryAsync<IReadOnlyList<MensagemReservada>>(async token =>
        {
            var expiradas = await outboxRepo.ExpirarPendentesAsync(politica, LimiteExpiracaoPorPrazo, token);
            var reclamadas = await outboxRepo.ReclamarLeasesVencidosAsync(LimiteLeasesPorReserva, token);
            if (expiradas + reclamadas > 0)
            {
                // Grava antes de reservar: a reserva lê o banco e as mensagens devolvidas à fila precisam estar lá.
                await unitOfWork.CommitAsync();
                logger.LogWarning("Dispatcher: {Expiradas} mensagens expiradas por prazo e {Reclamadas} reclamadas por lease vencido.",
                    expiradas, reclamadas);
            }

            var mensagens = await outboxRepo.ReservarParaEnvioAsync(batchSize, token);
            await unitOfWork.CommitAsync();
            return mensagens.Select(m => new MensagemReservada(m.EmpresaId, m.Id, m.Canal)).ToList();
        }, ct);
    }

    /// <summary>Passo 2: escopo da empresa com <c>try/catch</c> próprio. Nunca lança, salvo cancelamento do host.</summary>
    private async Task ProcessarItemAsync(MensagemReservada reservada, CancellationToken ct)
    {
        var estado = new EstadoDoItem();
        try
        {
            await ProcessarNoEscopoDaEmpresaAsync(reservada, estado, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: a mensagem segue EmEnvio e o lease devolve (ou fecha) na rodada seguinte.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dispatcher: falha ao processar a mensagem {OutboxId} (canal {Canal}).",
                reservada.Id, reservada.Canal);
            await RegistrarFalhaDoItemAsync(reservada, estado, ex, ct);
        }
    }

    private async Task ProcessarNoEscopoDaEmpresaAsync(MensagemReservada reservada, EstadoDoItem estado, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        // Antes da primeira conexão: o interceptor emite SET app.empresa_id na abertura.
        sp.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(reservada.EmpresaId);

        var outboxRepo = sp.GetRequiredService<IOutboxNotificacaoRepository>();
        var mensagem = await outboxRepo.ObterAsync(reservada.EmpresaId, reservada.Id, ct);
        // Outro processo já a tirou do EmEnvio (cancelada, reclamada pelo lease): não há o que enviar.
        if (mensagem is null || mensagem.Status != StatusOutbox.EmEnvio) return;

        var db = sp.GetRequiredService<EasyStockDbContext>();
        var logRepo = sp.GetRequiredService<ILogEnvioNotificacaoRepository>();
        var eventoRepo = sp.GetRequiredService<IEventoNotificacaoRepository>();
        var templateRepo = sp.GetRequiredService<ITemplateRepository>();
        var rotinaRepo = sp.GetRequiredService<IRotinaRepository>();
        var renderer = sp.GetRequiredService<IRendererTemplate>();
        var bloqueioRepo = sp.GetRequiredService<IBloqueioNotificacaoRepository>();
        var canais = sp.GetRequiredService<IEnumerable<ICanalNotificacao>>().ToList();

        await ProcessarMensagemAsync(
            mensagem, estado, canais, outboxRepo, logRepo, eventoRepo, templateRepo, rotinaRepo, bloqueioRepo, renderer, db, ct);
    }

    /// <summary>
    /// A falha vira estado terminal com o motivo, gravada num escopo limpo (o DbContext do item pode estar sujo): se o
    /// canal de entrega única (WhatsApp, SMS) já foi chamado, <c>Indeterminado</c>, porque pode ter saído; senão
    /// <c>Falhado</c>. Se nem isso grava, a mensagem segue <c>EmEnvio</c> e o lease a reclama.
    /// </summary>
    private async Task RegistrarFalhaDoItemAsync(
        MensagemReservada reservada, EstadoDoItem estado, Exception ex, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(reservada.EmpresaId);
            var outboxRepo = sp.GetRequiredService<IOutboxNotificacaoRepository>();
            var mensagem = await outboxRepo.ObterAsync(reservada.EmpresaId, reservada.Id, ct);
            if (mensagem is null || mensagem.Status != StatusOutbox.EmEnvio) return;

            var erro = $"Falha no processamento: {ex.GetType().Name}: {ex.Message}";
            if (estado.CanalChamado && reservada.Canal is CanalNotificacao.WhatsApp or CanalNotificacao.Sms)
                mensagem.MarcarIndeterminado(erro);
            else
                mensagem.MarcarFalhaTentativa(erro, TimeSpan.Zero, permanente: true);

            await sp.GetRequiredService<IUnitOfWork>().CommitAsync();
        }
        catch (Exception salvarEx) when (!ct.IsCancellationRequested)
        {
            logger.LogError(salvarEx,
                "Dispatcher: nem o estado de falha da mensagem {OutboxId} foi gravado; o lease a reclama.", reservada.Id);
        }
    }

    private async Task ProcessarMensagemAsync(
        OutboxMensagemNotificacao mensagem,
        EstadoDoItem estado,
        IList<ICanalNotificacao> canais,
        IOutboxNotificacaoRepository outboxRepo,
        ILogEnvioNotificacaoRepository logRepo,
        IEventoNotificacaoRepository eventoRepo,
        ITemplateRepository templateRepo,
        IRotinaRepository rotinaRepo,
        IBloqueioNotificacaoRepository bloqueioRepo,
        IRendererTemplate renderer,
        EasyStockDbContext db,
        CancellationToken ct)
    {
        var abriuFallback = false;
        var canal = canais.FirstOrDefault(c => c.Canal == mensagem.Canal);
        var bloqueio = await ObterBloqueioAtivoAsync(mensagem, bloqueioRepo, ct);
        if (bloqueio is not null)
        {
            // Kill switch ativado depois do enfileiramento (campanha enfileirada inteira): segura o que já está no outbox.
            logger.LogWarning(
                "Kill switch {Escopo} ativo — mensagem {OutboxId} suprimida (canal {Canal}).",
                bloqueio.EmpresaId is null ? "global" : "da empresa", mensagem.Id, mensagem.Canal);
            mensagem.Suprimir(
                $"Kill switch {(bloqueio.EmpresaId is null ? "global" : "da empresa")}: {bloqueio.Motivo}");
        }
        else if (canal is null)
        {
            logger.LogWarning(
                "Nenhum adapter registrado para canal {Canal} outbox={OutboxId}",
                mensagem.Canal, mensagem.Id);
            mensagem.Suprimir($"Canal {mensagem.Canal} sem adapter registrado");
        }
        else
        {
            abriuFallback = await EnviarERegistrarAsync(
                mensagem, estado, canal, logRepo, eventoRepo, templateRepo, rotinaRepo, renderer, db, ct);
        }

        await PurgarPayloadSeForAUltimaAsync(mensagem, abriuFallback, outboxRepo, eventoRepo, ct);

        await outboxRepo.UpdateAsync(mensagem, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// O bloqueio ativo (global ou da empresa, de todos os canais ou do canal da mensagem) que segura o envio, se houver.
    /// A empresa vai no <c>WHERE</c> do repositório: no Worker o filtro do EF está desligado.
    /// </summary>
    private static async Task<BloqueioNotificacao?> ObterBloqueioAtivoAsync(
        OutboxMensagemNotificacao mensagem, IBloqueioNotificacaoRepository bloqueioRepo, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var bloqueios = await bloqueioRepo.ListarAtivosAsync(mensagem.EmpresaId, mensagem.Canal, ct);
        return bloqueios.FirstOrDefault(b =>
            b.EstaAtivo(agora)
            && (b.Canal is null || b.Canal == mensagem.Canal)
            && (b.EmpresaId is null || b.EmpresaId == mensagem.EmpresaId));
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
        EstadoDoItem estado,
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
        estado.CanalChamado = true;
        var resultado = await canal.EnviarAsync(mensagemPronta, ct);
        sw.Stop();
        // O id do provider (wamid da Meta) vai no mesmo commit do resultado, qualquer que seja o desfecho: a N6 casa o
        // webhook de status por ele, inclusive o de uma entrega Indeterminada.
        mensagem.RegistrarProviderMensagemId(resultado.IdExterno);

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

        var evento = await eventoRepo.ObterAsync(mensagem.EmpresaId, mensagem.EventoId, ct);
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

        var evento = await eventoRepo.ObterAsync(mensagemOriginal.EmpresaId, mensagemOriginal.EventoId, ct);
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
