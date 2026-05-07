using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Integration;

/// <summary>
/// Implementação Postgres do <see cref="IPublicadorEventoIntegracao"/>.
/// Persiste evento no outbox transacional (<see cref="OutboxEventoIntegracao"/>).
///
/// <para>
/// Não chama <c>uow.CommitAsync</c> — o caller é responsável por commit
/// (transactional outbox: tudo na mesma transação do use case ou nada).
/// Se o caller esquecer commit, o evento some — comportamento esperado.
/// </para>
///
/// <para>
/// JSON serialization usa camelCase pra compatibilidade com payloads
/// consumidos por handlers que possam estar em outros runtimes futuros
/// (Python/Node integration adapters).
/// </para>
/// </summary>
public sealed class PublicadorEventoIntegracao(
    IOutboxEventoIntegracaoRepository repo,
    ILogger<PublicadorEventoIntegracao> logger) : IPublicadorEventoIntegracao
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task PublicarAsync<T>(
        Guid empresaId,
        string tipoEvento,
        string aggregateType,
        Guid aggregateId,
        T payload,
        int payloadSchemaVersion = 1,
        string? correlationId = null,
        Guid? causationEventId = null,
        CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(payload);

        var payloadJson = JsonSerializer.Serialize(payload, JsonOpts);

        var evento = OutboxEventoIntegracao.Criar(
            empresaId: empresaId,
            tipoEvento: tipoEvento,
            aggregateType: aggregateType,
            aggregateId: aggregateId,
            payloadJson: payloadJson,
            payloadSchemaVersion: payloadSchemaVersion,
            correlationId: correlationId,
            causationEventId: causationEventId);

        await repo.AddAsync(evento, ct);

        logger.LogDebug(
            "OutboxEventoIntegracao publicado: {TipoEvento} aggregate={AggregateType}:{AggregateId} eventId={EventId}.",
            tipoEvento, aggregateType, aggregateId, evento.Id);
    }
}
