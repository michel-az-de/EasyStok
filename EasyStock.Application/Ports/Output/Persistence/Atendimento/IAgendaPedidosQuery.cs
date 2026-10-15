namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

public sealed record AgendaPedidoLeitura(Guid JanelaId, DateOnly Data, Guid PedidoId, string? ClienteNome, string Status);

public interface IAgendaPedidosQuery
{
    Task<IReadOnlyList<AgendaPedidoLeitura>> ListarAsync(
        Guid empresaId, Guid storefrontId, DateOnly de, DateOnly ate, CancellationToken ct = default);
}
