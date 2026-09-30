using EasyStock.Application.Ports.Output.Atendimento;

namespace EasyStock.Application.Services.Operacao;

/// <summary>
/// Implementação padrão de <see cref="IOperacaoEventPublisher"/> para hosts sem o SSE de operação (Worker,
/// testes de integração). A Api troca por <c>OperacaoEventPublisher</c> sobre o broker in-memory (S18).
/// </summary>
public sealed class NoOpOperacaoEventPublisher : IOperacaoEventPublisher
{
    public Task PublicarAsync(string evento, Guid empresaId, object payload, CancellationToken ct = default) =>
        Task.CompletedTask;
}
