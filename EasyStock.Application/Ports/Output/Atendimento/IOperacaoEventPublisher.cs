namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Publica eventos de operação (SSE, S18) após o commit — ex.: <c>conversa.mensagem_recebida</c>
/// pro console atualizar a inbox em tempo real. Nomes e payloads em <see cref="EventosOperacao"/>.
/// Na Api vai para o broker in-memory (<c>GET api/operacao/eventos</c>); nos demais hosts é no-op.
/// </summary>
public interface IOperacaoEventPublisher
{
    Task PublicarAsync(string evento, Guid empresaId, object payload, CancellationToken ct = default);
}
