namespace EasyStock.Application.Ports.Output.Integration;

/// <summary>
/// Despacha eventos pendentes do outbox de integração. Invocado por
/// background workers em loop ou trigger externo (admin "process now").
///
/// <para>
/// Cada chamada a <see cref="ExecutarRodadaAsync"/> processa até
/// <paramref name="batchSize"/> eventos pendentes globalmente (cross-tenant).
/// Eventos sem handler registrado pro <c>TipoEvento</c> são marcados como
/// <see cref="Domain.Integration.StatusOutboxIntegracao.Falhado"/> com erro
/// "no handler" — admin reprocessa via UI quando o handler for adicionado.
/// </para>
/// </summary>
public interface IIntegrationEventDispatcher
{
    /// <summary>
    /// Processa próximos N pendentes. Retorna a quantidade processada
    /// (sucessos + falhas + sem-handler). Caller decide se chama de novo
    /// imediatamente (se voltou batchSize cheio) ou aguarda próximo tick.
    /// </summary>
    Task<int> ExecutarRodadaAsync(int batchSize, CancellationToken ct);
}
