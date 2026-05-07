namespace EasyStock.Domain.Sales;

/// <summary>
/// Máquina de estados do agregado <see cref="Entities.Pedido"/>. Centraliza
/// em um lugar só: transições válidas, status considerados "abertos" (em curso),
/// status finais, e status que sinalizam estoque descontado.
///
/// <para>
/// Use <see cref="EnsureTransicaoValida"/> antes de mudar status no agregado.
/// Esse método garante que <see cref="TransicaoInvalidaException"/> seja lançada
/// na borda do agregado, não no meio da lógica de aplicação.
/// </para>
///
/// <para>
/// Reflete fielmente o comportamento atual do ERP (Casa da Babá em produção):
/// transições agora vivem no domínio em vez de uma matriz hardcoded dentro
/// de <c>AtualizarStatusPedidoUseCase</c>.
/// </para>
/// </summary>
public static class PedidoStateMachine
{
    /// <summary>
    /// Transições válidas: estado de origem → conjunto de destinos permitidos.
    ///
    /// <list type="bullet">
    ///   <item>Aguardando → {Preparando, Cancelado}</item>
    ///   <item>Preparando → {Pronto, Cancelado}</item>
    ///   <item>Pronto → {Entregue, Cancelado}</item>
    ///   <item>Entregue → {Cancelado}  (cancela pós-entrega devolve estoque)</item>
    ///   <item>Cancelado → ∅</item>
    /// </list>
    /// </summary>
    public static IReadOnlyDictionary<StatusPedido, IReadOnlySet<StatusPedido>> Transicoes { get; } =
        new Dictionary<StatusPedido, IReadOnlySet<StatusPedido>>
        {
            [StatusPedido.Aguardando] = new HashSet<StatusPedido> { StatusPedido.Preparando, StatusPedido.Cancelado },
            [StatusPedido.Preparando] = new HashSet<StatusPedido> { StatusPedido.Pronto, StatusPedido.Cancelado },
            [StatusPedido.Pronto] = new HashSet<StatusPedido> { StatusPedido.Entregue, StatusPedido.Cancelado },
            [StatusPedido.Entregue] = new HashSet<StatusPedido> { StatusPedido.Cancelado },
            [StatusPedido.Cancelado] = new HashSet<StatusPedido>(),
        };

    /// <summary>
    /// Status que indicam pedido "em curso". Usado por
    /// <see cref="Application.Ports.Output.Persistence.IPedidoRepository.ExistemPedidosAbertosComProdutoAsync"/>
    /// pra bloquear inativação de produto que orfanaria itens em produção.
    /// </summary>
    public static IReadOnlySet<StatusPedido> Abertos { get; } =
        new HashSet<StatusPedido> { StatusPedido.Aguardando, StatusPedido.Preparando, StatusPedido.Pronto };

    /// <summary>Status terminais — não há mais transição saindo.</summary>
    public static IReadOnlySet<StatusPedido> Finais { get; } =
        new HashSet<StatusPedido> { StatusPedido.Entregue, StatusPedido.Cancelado };

    /// <summary>
    /// Status nos quais o estoque do pedido já está descontado. Usado por
    /// <c>PedidoEstoqueIntegrationService</c> pra decidir entre desconto novo
    /// ou devolução na transição.
    /// </summary>
    public static IReadOnlySet<StatusPedido> ComEstoqueDescontado { get; } =
        new HashSet<StatusPedido> { StatusPedido.Pronto, StatusPedido.Entregue };

    public static bool PodeTransicionar(StatusPedido de, StatusPedido para)
        => Transicoes.TryGetValue(de, out var destinos) && destinos.Contains(para);

    /// <summary>
    /// Lança <see cref="TransicaoInvalidaException"/> se a transição não for
    /// permitida. Idempotência (de == para) NÃO é validada aqui — fica
    /// responsabilidade do caller decidir se trata como no-op ou erro.
    /// </summary>
    public static void EnsureTransicaoValida(StatusPedido de, StatusPedido para)
    {
        if (!PodeTransicionar(de, para))
            throw new TransicaoInvalidaException(de, para);
    }

    public static bool EstaAberto(StatusPedido status) => Abertos.Contains(status);
    public static bool EstaFinalizado(StatusPedido status) => Finais.Contains(status);
    public static bool DescontaEstoque(StatusPedido status) => ComEstoqueDescontado.Contains(status);
}
