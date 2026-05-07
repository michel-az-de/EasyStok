namespace EasyStock.Domain.Integration;

/// <summary>
/// Estados do <see cref="OutboxEventoIntegracao"/>. Workers (dispatchers
/// que consomem o outbox) avançam pelos estados em ordem; falhas voltam
/// pra Pendente até esgotar tentativas, quando vão pra Falhado.
/// </summary>
public enum StatusOutboxIntegracao
{
    /// <summary>Aguardando primeira tentativa ou retry.</summary>
    Pendente = 1,

    /// <summary>Worker pegou o item e está despachando agora.</summary>
    EmEnvio = 2,

    /// <summary>Despachado com sucesso.</summary>
    Enviado = 3,

    /// <summary>Esgotou tentativas — requer intervenção manual via admin.</summary>
    Falhado = 4,

    /// <summary>Cancelado manualmente (ex: evento obsoleto descartado por admin).</summary>
    Cancelado = 5,
}
