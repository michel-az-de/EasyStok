namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// #3294: os números da tela Pedidos de Cadastros, calculados no banco sobre os mesmos filtros da lista.
/// Toda leitura começa pela empresa do filtro; nada de outra empresa entra em soma, contagem ou ordem.
/// </summary>
public interface IPedidosResumoQuery
{
    Task<ResumoDosPedidos> ResumirAsync(FiltroDosPedidos filtro, DateTime agoraUtc, CancellationToken ct = default);

    /// <summary>Só quantidade e vendido, para comparar com o período anterior.</summary>
    Task<VendasDoPeriodo> VendasAsync(FiltroDosPedidos filtro, CancellationToken ct = default);

    /// <summary>Sinais de uma página da lista: a ordem da compra do cliente e desde quando o pedido está na situação.</summary>
    Task<IReadOnlyDictionary<Guid, SinaisDoPedido>> SinaisAsync(Guid empresaId, IReadOnlyCollection<Guid> pedidoIds, CancellationToken ct = default);
}

/// <summary>Os filtros da lista, sem página nem ordem. O resumo não recorta por situação (os indicadores filtram por ela).</summary>
public sealed record FiltroDosPedidos(
    Guid EmpresaId,
    Guid? ClienteId = null,
    DateTime? Desde = null,
    DateTime? Ate = null,
    string? Search = null,
    DateTime? EntregaDesde = null,
    DateTime? EntregaAte = null);

public sealed record ResumoDosPedidos(
    int Pedidos,
    int Vendas,
    decimal Vendido,
    decimal AReceberValor,
    int AReceberPedidos,
    int AguardandoPagamento,
    int AguardandoAprovacao,
    int EmAndamento,
    int Atrasados,
    int CanceladosPedidos,
    decimal CanceladosValor,
    int PrimeiraCompra,
    int Recompra,
    IReadOnlyList<ItemMaisVendido> MaisVendidos);

public sealed record ItemMaisVendido(string Nome, decimal Quantidade);

public sealed record VendasDoPeriodo(int Pedidos, decimal Vendido);

public sealed record SinaisDoPedido(int? CompraDoCliente, DateTime? SituacaoDesde);
