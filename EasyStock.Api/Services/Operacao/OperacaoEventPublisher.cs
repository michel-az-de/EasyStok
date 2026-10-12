using EasyStock.Application.Ports.Output.Atendimento;

namespace EasyStock.Api.Services.Operacao;

/// <summary>
/// Publica os eventos de operação (S18) no <see cref="OperacaoEventBroker"/> in-memory. Evento de UI: falha
/// aqui só é registrada, nunca desfaz nem derruba a operação que já foi gravada.
/// </summary>
public sealed class OperacaoEventPublisher(OperacaoEventBroker broker, ILogger<OperacaoEventPublisher> logger)
    : IOperacaoEventPublisher
{
    public Task PublicarAsync(string evento, Guid empresaId, object payload, CancellationToken ct = default)
    {
        try
        {
            broker.PublicarOperacao(empresaId, evento, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao publicar evento de operacao {Evento} empresa={EmpresaId}", evento, empresaId);
        }
        return Task.CompletedTask;
    }
}
