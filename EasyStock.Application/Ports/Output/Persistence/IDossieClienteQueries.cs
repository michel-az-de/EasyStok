namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>Item de pedido como o dossiê precisa: nome do snapshot e quantidade.</summary>
public sealed record ItemPedidoResumo(string Nome, decimal Quantidade);

/// <summary>Pedido do cliente em projeção leve para o dossiê (S25).</summary>
public sealed record PedidoResumoCliente(
    Guid Id, string Status, DateTime CriadoEm, decimal Total, IReadOnlyList<ItemPedidoResumo> Itens);

/// <summary>
/// Outro cadastro no mesmo endereço (S25, D10): só id e nome. Nunca o histórico, os pedidos ou as
/// notas do outro cadastro; também não funde cadastros.
/// </summary>
public sealed record ClienteMesmoDomicilio(Guid ClienteId, string Nome);

/// <summary>Pedidos do cliente para o dossiê (S25). <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).</summary>
public interface IHistoricoPedidosClienteQueries
{
    /// <summary>Os <paramref name="maximo"/> pedidos mais recentes do cliente, mais novo primeiro, com itens.</summary>
    Task<IReadOnlyList<PedidoResumoCliente>> ListarAsync(Guid empresaId, Guid clienteId, int maximo, CancellationToken ct = default);
}

/// <summary>
/// Sinal de mesmo domicílio (S25, RN-13): derivado do endereço normalizado
/// (<see cref="ClienteEndereco.ChaveDomicilio"/>), sem tabela própria.
/// </summary>
public interface IDomicilioQueries
{
    /// <summary>Outros clientes ativos da empresa com algum endereço de mesma chave; vazio sem endereço completo.</summary>
    Task<IReadOnlyList<ClienteMesmoDomicilio>> ListarMesmoDomicilioAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default);
}
