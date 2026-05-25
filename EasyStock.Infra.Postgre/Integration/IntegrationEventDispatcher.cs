using System.Diagnostics;
using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Integration;

/// <summary>
/// Implementação Postgres do <see cref="IIntegrationEventDispatcher"/>.
///
/// <para>
/// <b>Resolução de handler</b>: lookup keyed em <see cref="IServiceProvider"/>
/// pelo <see cref="OutboxEventoIntegracao.TipoEvento"/>. Múltiplos handlers
/// pra mesmo tipo (ex: webhook + métrica) são suportados — todos rodam em
/// sequência, primeiro throw aborta (sem rollback dos anteriores —
/// idempotência é responsabilidade do handler).
/// </para>
///
/// <para>
/// <b>Backoff exponencial</b>: tentativa N agenda próxima em
/// <c>30s * 2^(N-1)</c> com jitter ±20%. Limite N=5 (default
/// <see cref="OutboxEventoIntegracao.MaxTentativas"/>) é aceitável pra
/// providers externos comuns; sagas longas devem usar MaxTentativas alto.
/// </para>
///
/// <para>
/// <b>Métricas OTel</b> (prefixo <c>easystock.integration.outbox</c>):
/// <list type="bullet">
///   <item><c>.dispatched.total</c> counter — total processados (sucesso+falha)</item>
///   <item><c>.success.total</c> counter</item>
///   <item><c>.failed.total</c> counter (incluindo no-handler)</item>
///   <item><c>.duration.ms</c> histogram por evento</item>
///   <item><c>.lag.seconds</c> gauge — idade do mais antigo pendente</item>
/// </list>
/// </para>
/// </summary>
public sealed class IntegrationEventDispatcher : IIntegrationEventDispatcher
{
    public const string MeterName = "EasyStock.Integration.Outbox";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> Dispatched = Meter.CreateCounter<long>("easystock.integration.outbox.dispatched.total");
    private static readonly Counter<long> Success = Meter.CreateCounter<long>("easystock.integration.outbox.success.total");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("easystock.integration.outbox.failed.total");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("easystock.integration.outbox.duration.ms");

    private readonly IOutboxEventoIntegracaoRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IntegrationEventDispatcher> _logger;

    public IntegrationEventDispatcher(
        IOutboxEventoIntegracaoRepository repo,
        IUnitOfWork uow,
        IServiceProvider serviceProvider,
        ILogger<IntegrationEventDispatcher> logger)
    {
        _repo = repo;
        _uow = uow;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<int> ExecutarRodadaAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize <= 0) return 0;

        // Single shard nesta versão. Multi-shard com advisory lock fica pra
        // F4.d quando volume justificar (>10k eventos pendentes simultâneos).
        var pendentes = new List<OutboxEventoIntegracao>();
        for (int shard = 0; shard < 4 && pendentes.Count < batchSize; shard++)
        {
            var batch = await _repo.ProximosPendentesAsync(shard, batchSize - pendentes.Count, ct);
            pendentes.AddRange(batch);
        }

        if (pendentes.Count == 0) return 0;

        int processados = 0;
        foreach (var evt in pendentes)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessarEventoAsync(evt, ct);
            processados++;
        }

        return processados;
    }

    private async Task ProcessarEventoAsync(OutboxEventoIntegracao evt, CancellationToken ct)
    {
        var tags = new TagList
        {
            { "tipo_evento", evt.TipoEvento },
            { "aggregate_type", evt.AggregateType },
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Dispatched.Add(1, tags);

        try
        {
            evt.MarcarEmEnvio();
            await _repo.UpdateAsync(evt, ct);
            await _uow.CommitAsync();

            // Resolve handlers keyed pelo TipoEvento. Suporta múltiplos handlers
            // pro mesmo tipo (ex: um pra webhook, outro pra métrica).
            var handlers = _serviceProvider
                .GetKeyedServices<IIntegrationEventHandler>(evt.TipoEvento)
                .ToList();

            if (handlers.Count == 0)
            {
                evt.MarcarFalhaTentativa(
                    $"Nenhum IIntegrationEventHandler registrado pra TipoEvento '{evt.TipoEvento}'.",
                    TimeSpan.FromMinutes(5));
                _logger.LogWarning(
                    "Outbox {EventId} sem handler — TipoEvento {TipoEvento} agg={Agg}:{AggId}.",
                    evt.Id, evt.TipoEvento, evt.AggregateType, evt.AggregateId);
                Failed.Add(1, tags);
            }
            else
            {
                foreach (var handler in handlers)
                {
                    await handler.HandleAsync(evt, ct);
                }
                evt.MarcarEnviado();
                Success.Add(1, tags);
                _logger.LogDebug(
                    "Outbox {EventId} despachado: {TipoEvento} ({HandlerCount} handler(s)).",
                    evt.Id, evt.TipoEvento, handlers.Count);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelamento limpo — não conta como falha; deixa pendente
            // pra próxima rodada. Não atualiza estado.
            throw;
        }
        catch (Exception ex)
        {
            var backoff = ComputarBackoff(evt.Tentativas + 1);
            evt.MarcarFalhaTentativa(ex.GetType().Name + ": " + ex.Message, backoff);
            _logger.LogError(ex,
                "Outbox {EventId} falhou tentativa {Tentativa}/{MaxTentativas} ({TipoEvento}). Próxima em {Backoff}.",
                evt.Id, evt.Tentativas, evt.MaxTentativas, evt.TipoEvento, backoff);
            Failed.Add(1, tags);
        }
        finally
        {
            try
            {
                await _repo.UpdateAsync(evt, ct);
                await _uow.CommitAsync();
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx,
                    "Outbox {EventId} persistência do estado pós-handler falhou — risco de re-processo.",
                    evt.Id);
            }

            sw.Stop();
            Duration.Record(sw.Elapsed.TotalMilliseconds, tags);
        }
    }

    /// <summary>
    /// Backoff exponencial com jitter: 30s * 2^(N-1) ± 20%.
    /// Tentativa 1 → ~24-36s; tentativa 5 → ~6.4-9.6min.
    /// </summary>
    private static TimeSpan ComputarBackoff(int tentativaProxima)
    {
        var baseSeconds = 30.0 * Math.Pow(2, Math.Max(0, tentativaProxima - 1));
        var jitter = (Random.Shared.NextDouble() * 0.4) - 0.2; // -20% a +20%
        return TimeSpan.FromSeconds(baseSeconds * (1 + jitter));
    }
}
