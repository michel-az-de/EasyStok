namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <param name="NumeroComanda">#1883: número da conversa dona do pedido ("Comanda N"), pela mesma regra de
/// <c>IConversaRepository.ObterIdPorPedidoAsync</c>, resolvido em lote. Nulo quando nenhuma conversa é dona dele.</param>
/// <param name="NumeroDoDia">#1641-1: número do dia (S53); nulo até o pedido entrar na fila ou ser pago.</param>
public sealed record AgendaPedidoLeitura(Guid JanelaId, DateOnly Data, Guid PedidoId, string? ClienteNome, string Status,
    long? NumeroComanda = null, int? NumeroDoDia = null);

public interface IAgendaPedidosQuery
{
    Task<IReadOnlyList<AgendaPedidoLeitura>> ListarAsync(
        Guid empresaId, Guid storefrontId, DateOnly de, DateOnly ate, CancellationToken ct = default);
}
