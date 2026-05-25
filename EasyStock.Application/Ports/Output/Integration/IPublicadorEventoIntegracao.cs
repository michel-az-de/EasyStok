namespace EasyStock.Application.Ports.Output.Integration;

/// <summary>
/// Publica eventos de integração externa via outbox transacional. Use cases
/// e sagas devem publicar via este port — nunca chamar provider externo
/// diretamente da transação principal (acopla disponibilidade do provider
/// ao commit do use case).
///
/// <para>
/// O dispatcher (BackgroundService) consome o outbox e despacha de forma
/// resiliente (retry/circuit-breaker via Polly). Falhas no provider externo
/// não impedem o use case de completar.
/// </para>
///
/// <para>
/// <b>Vs <c>IPublicadorEventos</c></b>: aquele é in-memory pub/sub interno
/// pra eventos de domínio que casam handlers no MESMO processo
/// (ex: PedidoCriado → ProdutoEstatisticas). Este aqui é pra eventos que
/// CRUZAM bordas de processo: integrações externas, sagas longas,
/// observabilidade fora do request.
/// </para>
/// </summary>
public interface IPublicadorEventoIntegracao
{
    /// <summary>
    /// Persiste evento no outbox (mesma transação do use case caller).
    /// Não despacha imediatamente — dispatcher pega e processa async.
    ///
    /// <para>
    /// <paramref name="payload"/> é serializado em JSON. Use schemas
    /// estáveis e versione via <paramref name="payloadSchemaVersion"/>
    /// quando incompatibly mudar.
    /// </para>
    /// </summary>
    Task PublicarAsync<T>(
        Guid empresaId,
        string tipoEvento,
        string aggregateType,
        Guid aggregateId,
        T payload,
        int payloadSchemaVersion = 1,
        string? correlationId = null,
        Guid? causationEventId = null,
        CancellationToken ct = default) where T : class;
}
