namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <param name="NumeroComanda">#1883: número da conversa dona do pedido ("Comanda N"), pela mesma regra de
/// <c>IConversaRepository.ObterIdPorPedidoAsync</c>, resolvido em lote. Nulo quando nenhuma conversa é dona dele.</param>
public sealed record AgendaPedidoLeitura(Guid JanelaId, DateOnly Data, Guid PedidoId, string? ClienteNome, string Status,
    long? NumeroComanda = null);

public interface IAgendaPedidosQuery
{
    Task<IReadOnlyList<AgendaPedidoLeitura>> ListarAsync(
        Guid empresaId, Guid storefrontId, DateOnly de, DateOnly ate, CancellationToken ct = default);
}
