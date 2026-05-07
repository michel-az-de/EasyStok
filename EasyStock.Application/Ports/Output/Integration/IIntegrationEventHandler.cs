using EasyStock.Domain.Integration;

namespace EasyStock.Application.Ports.Output.Integration;

/// <summary>
/// Handler de evento de integração externa. Implementações registram-se via
/// keyed DI com a chave igual ao <see cref="OutboxEventoIntegracao.TipoEvento"/>
/// que tratam (ex: <c>"pedido.confirmado"</c>, <c>"pagamento.capturado"</c>).
///
/// <para>
/// O dispatcher (<see cref="IIntegrationEventDispatcher"/>) resolve o handler
/// pelo TipoEvento, deserializa o payload conforme contrato do handler, e
/// invoca <see cref="HandleAsync"/>. Throw aborta a tentativa e agenda retry
/// com backoff exponencial. Sucesso (sem throw) marca o evento como Enviado.
/// </para>
///
/// <para>
/// <b>Idempotência</b>: handlers DEVEM ser idempotentes. O mesmo evento pode
/// ser processado múltiplas vezes (retry após falha de rede, restart de
/// worker, etc.) — usar <see cref="OutboxEventoIntegracao.IdempotencyKey"/>
/// ou <see cref="OutboxEventoIntegracao.Id"/> como chave de dedup quando
/// chamar serviços externos (Idempotency-Key HTTP header, etc.).
/// </para>
/// </summary>
public interface IIntegrationEventHandler
{
    /// <summary>
    /// Tipo de evento que este handler processa. Constante (não muda em runtime).
    /// Mesmo handler pode tratar múltiplos tipos via wildcard só se o caller
    /// registrar como tal — por convenção, 1 classe = 1 tipo.
    /// </summary>
    string TipoEvento { get; }

    /// <summary>
    /// Processa o evento. Retorno normal = sucesso (dispatcher marca Enviado).
    /// Throw = falha (dispatcher incrementa Tentativas e reagenda com backoff).
    /// </summary>
    Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct);
}
