using EasyStock.Application.Ports.Output.Atendimento;

namespace EasyStock.Infra.Async;

/// <summary>Implementação no-op de <see cref="IOperacaoEventPublisher"/> até S18 (SSE) existir.</summary>
public sealed class NoOpOperacaoEventPublisher : IOperacaoEventPublisher
{
    public Task PublicarAsync(string evento, Guid empresaId, object payload, CancellationToken ct = default) =>
        Task.CompletedTask;
}
