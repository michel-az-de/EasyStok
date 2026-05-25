using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Implementacao Onda P0: sempre <see cref="EstadoSaudeGateway.Saudavel"/>,
/// nao acumula metricas. Em P1 substituido por <c>InMemoryGatewayHealthStore</c>
/// (Singleton com janela rolante 5min + flush DB cada 30s).
/// </summary>
public sealed class NoopGatewayHealthStore : IGatewayHealthStore
{
    public GatewayHealthSnapshotMem Get(string provedor) =>
        new(provedor, EstadoSaudeGateway.Saudavel, SuccessRate: 1.0, LatenciaP95Ms: 0, SuspensoAte: null);

    public void RegistrarSucesso(string provedor, int latenciaMs) { /* P0 no-op */ }

    public void RegistrarFalha(string provedor, ErrorCategory categoria, int latenciaMs) { /* P0 no-op */ }

    public bool PodeUsar(string provedor) => true;

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
