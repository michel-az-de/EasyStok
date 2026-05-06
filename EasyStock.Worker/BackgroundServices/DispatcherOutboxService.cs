using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Dispatcher do Outbox — lê lotes de OutboxMensagemNotificacao e envia via ICanalNotificacao.
/// Usa advisory lock por shard para safe multi-réplica (4 shards × 4 réplicas = sem sobreposição).
/// </summary>
public sealed class DispatcherOutboxService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<DispatcherOutboxService> logger) : BackgroundService
{
    // Base de lock: 0x4E4F5449 = "NOTI" em ASCII
    private const long LockBase = 0x4E4F_5449_0000_0000L;

    private static readonly JsonSerializerOptions EnumOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        logger.LogInformation(
            "DispatcherOutboxService iniciado — shards={Shards} batch={Batch}",
            opts.ShardCount, opts.DispatcherBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Aguarda sinal de LISTEN/NOTIFY ou timeout de polling
            await OutboxListenService.NotifySignal.WaitAsync(stoppingToken);

            for (var shard = 0; shard < opts.ShardCount; shard++)
            {
                if (stoppingToken.IsCancellationRequested) break;
                await ProcessarShardAsync(shard, opts.DispatcherBatchSize, stoppingToken);
            }
        }
    }

    private async Task ProcessarShardAsync(int shardKey, int batchSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<EasyStockDbContext>();
        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>();

        await advisoryLock.TentarExecutarAsync(LockBase + shardKey, async token =>
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
                logger.LogInformation(
                    "Shard {Shard}: processadas {Count} mensagens.", shardKey, mensagens.Count);

        }, ct);
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
        var canal = canais.FirstOrDefault(c => c.Canal == mensagem.Canal);
        if (canal is null)
        {
            logger.LogWarning(
                "Nenhum adapter registrado para canal {Canal} outbox={OutboxId}",
                mensagem.Canal, mensagem.Id);
            mensagem.Suprimir($"Canal {mensagem.Canal} sem adapter registrado");
            await outboxRepo.UpdateAsync(mensagem, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        var mensagemPronta = new MensagemPronta(
            mensagem.Id, mensagem.Destinatario, mensagem.AssuntoRenderizado,
            mensagem.CorpoRenderizado, mensagem.Canal, mensagem.Categoria);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resultado = await canal.EnviarAsync(mensagemPronta, ct);
        sw.Stop();

        if (resultado.Sucesso)
        {
            mensagem.MarcarEnviado(resultado.ProviderUsado ?? mensagem.Canal.ToString());
            var logSucesso = LogEnvioNotificacao.RegistrarSucesso(
                mensagem.Id, mensagem.Tentativas + 1, mensagem.Canal,
                resultado.ProviderUsado ?? mensagem.Canal.ToString(),
                sw.ElapsedMilliseconds,
                resultado.StatusHttp,
                resultado.RespostaProviderJson,
                mensagem.Categoria == CategoriaConteudoNotificacao.Transacional);

            await logRepo.AddAsync(logSucesso, ct);
        }
        else
        {
            var backoff = mensagem.Tentativas switch
            {
                0 => TimeSpan.FromMinutes(1),
                1 => TimeSpan.FromMinutes(5),
                _ => TimeSpan.FromMinutes(30)
            };

            mensagem.MarcarFalhaTentativa(resultado.ErroDetalhado ?? "Erro desconhecido", backoff);

            var logFalha = LogEnvioNotificacao.RegistrarFalha(
                mensagem.Id, mensagem.Tentativas, mensagem.Canal,
                resultado.ProviderUsado ?? mensagem.Canal.ToString(),
                sw.ElapsedMilliseconds,
                resultado.ErroDetalhado ?? "Erro desconhecido",
                resultado.StatusHttp);
            logFalha.BypassConsentimento = mensagem.Categoria == CategoriaConteudoNotificacao.Transacional;

            await logRepo.AddAsync(logFalha, ct);

            // Fallback para próximo canal quando todas as tentativas foram esgotadas
            if (mensagem.TentativasEsgotadas())
                await TentarFallbackCanalAsync(mensagem, eventoRepo, templateRepo, rotinaRepo, renderer, db, ct);
        }

        await outboxRepo.UpdateAsync(mensagem, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task TentarFallbackCanalAsync(
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
            return;
        }

        if (fallbacks.Count == 0) return;

        var proximoCanal = fallbacks[0];
        var fallbackRestantes = fallbacks.Skip(1).ToList();

        var evento = await eventoRepo.GetByIdAsync(mensagemOriginal.EventoId, ct);
        if (evento is null) return;

        var rotina = (await rotinaRepo.ListarAtivasAsync(evento.Tipo, ct))
            .FirstOrDefault(r => r.EmpresaId == evento.EmpresaId || r.EmpresaId == null);
        if (rotina is null) return;

        var template = await templateRepo.GetAtivoAsync(rotina.TemplateCodigo, proximoCanal, evento.EmpresaId, ct)
            ?? await templateRepo.GetAtivoAsync(rotina.TemplateCodigo, proximoCanal, null, ct);
        if (template is null)
        {
            logger.LogWarning(
                "Fallback canal {Canal}: template '{Codigo}' não encontrado — cancelando fallback.",
                proximoCanal, rotina.TemplateCodigo);
            return;
        }

        var vars = ParsePayload(evento.PayloadJson);
        string assunto, corpo;
        try
        {
            assunto = await renderer.RenderizarAsync(template.AssuntoTemplate, vars, ct);
            corpo = await renderer.RenderizarAsync(template.CorpoTemplate, vars, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao renderizar template para fallback canal {Canal}", proximoCanal);
            return;
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
