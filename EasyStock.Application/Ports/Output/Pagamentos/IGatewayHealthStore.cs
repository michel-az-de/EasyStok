using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Saude por gateway — alimentada por <c>MeasuredPagamentoGatewayDecorator</c>
/// e consumida pelo <see cref="IPagamentoGatewayRouter"/> para filtrar gateways
/// suspensos.
///
/// <para>
/// <b>Onda P0</b>: implementacao no-op (<c>NoopGatewayHealthStore</c>) — todos
/// os gateways sao considerados <see cref="EstadoSaudeGateway.Saudavel"/> por
/// default. Em P1, <c>InMemoryGatewayHealthStore</c> Singleton com janela
/// rolante de 5min + flush em-DB cada 30s para audit.
/// </para>
/// </summary>
public interface IGatewayHealthStore
{
    /// <summary>Snapshot atual da saude (em-memoria; cache local em P1).</summary>
    GatewayHealthSnapshotMem Get(string provedor);

    /// <summary>Registra chamada bem-sucedida ao gateway.</summary>
    void RegistrarSucesso(string provedor, int latenciaMs);

    /// <summary>Registra falha classificada.</summary>
    void RegistrarFalha(string provedor, ErrorCategory categoria, int latenciaMs);

    /// <summary>
    /// Decisao do router: pode-se usar este gateway? Em P0 sempre true.
    /// Em P1, false se circuit aberto (Estado=Suspenso).
    /// </summary>
    bool PodeUsar(string provedor);

    /// <summary>Flush para tabela <c>gateway_health_snapshots</c> (P1).</summary>
    Task FlushAsync(CancellationToken ct = default);
}

/// <summary>Snapshot em-memoria leve para decisao de routing.</summary>
public sealed record GatewayHealthSnapshotMem(
    string Provedor,
    EstadoSaudeGateway Estado,
    double SuccessRate,
    int LatenciaP95Ms,
    DateTime? SuspensoAte);
