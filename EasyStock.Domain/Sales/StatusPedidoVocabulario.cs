namespace EasyStock.Domain.Sales;

/// <summary>
/// Classificação operacional do status para agrupamento/UX (independe do rótulo textual).
/// </summary>
public enum BucketStatusPedido
{
    /// <summary>Ainda não entrou na fila de trabalho: montagem, pagamento online pendente
    /// ou aprovação do cardápio. Não recebe pagamento manual (ver
    /// <see cref="PedidoStateMachine.AceitaPagamento"/>).</summary>
    PreOperacional,
    /// <summary>Em curso na operação (fila ERP): aguardando, preparando, pronto, aprovado.</summary>
    Operacional,
    /// <summary>Terminal: entregue ou cancelado.</summary>
    Terminal,
}

/// <summary>
/// <strong>Fonte única do vocabulário de status do pedido</strong> (issue 863, Onda 1 do
/// ADR-0042). Para cada <see cref="StatusPedido"/> define o rótulo por audiência
/// (lojista / cliente-storefront) e o <see cref="BucketStatusPedido">bucket</see>.
///
/// <para>
/// Antes existiam 4 dicionários divergentes: <see cref="StatusPedidoMapper"/> (string de
/// fio), o <c>StatusToContract</c> do storefront, o <c>StatusHelper</c> do Web e o
/// <c>STATUS_LABEL</c> do cockpit JS. Este tipo centraliza o <em>conceito</em>; cada
/// superfície mapeia para a sua UI (ex.: badge CSS no Web) e é travada por teste de drift.
/// O badge CSS NÃO vive aqui — é decisão de apresentação, fica no Web.
/// </para>
/// </summary>
public static class StatusPedidoVocabulario
{
    /// <param name="RotuloLojista">Rótulo exibido ao lojista (Web/cockpit/Detail).</param>
    /// <param name="ContratoCliente">Token PascalCase do contrato storefront consumido pelo
    /// app do cliente (casa-da-baba). Colapsa <c>aguardando</c>+<c>preparando</c> em
    /// <c>EmPreparo</c> e <c>pronto</c> em <c>SaiuParaEntrega</c> de propósito — o cliente
    /// não vê a granularidade interna da cozinha.</param>
    /// <param name="Bucket">Classificação operacional.</param>
    public sealed record Projecao(string RotuloLojista, string ContratoCliente, BucketStatusPedido Bucket);

    private static readonly IReadOnlyDictionary<StatusPedido, Projecao> Mapa =
        new Dictionary<StatusPedido, Projecao>
        {
            // Pré-operacionais (fluxo storefront antes de entrar na fila)
            [StatusPedido.Rascunho]                = new("Rascunho",              "Rascunho",               BucketStatusPedido.PreOperacional),
            [StatusPedido.AguardandoPagamento]     = new("Aguardando pagamento",  "AguardandoPagamento",    BucketStatusPedido.PreOperacional),
            [StatusPedido.AguardandoAprovacaoBaba] = new("Aguardando aprovação",  "AguardandoAprovacaoBaba", BucketStatusPedido.PreOperacional),
            // Operacionais (fila ERP)
            [StatusPedido.AprovadoBaba]            = new("Aprovado",              "AprovadoBaba",           BucketStatusPedido.Operacional),
            [StatusPedido.Aguardando]              = new("Aguardando",            "EmPreparo",              BucketStatusPedido.Operacional),
            [StatusPedido.Preparando]              = new("Em preparo",            "EmPreparo",              BucketStatusPedido.Operacional),
            [StatusPedido.Pronto]                  = new("Pronto",                "SaiuParaEntrega",        BucketStatusPedido.Operacional),
            // Terminais
            [StatusPedido.Entregue]                = new("Entregue",              "Entregue",               BucketStatusPedido.Terminal),
            [StatusPedido.Cancelado]               = new("Cancelado",             "Cancelado",              BucketStatusPedido.Terminal),
        };

    /// <summary>Projeção completa do status. Lança se o enum crescer sem ser mapeado aqui.</summary>
    public static Projecao De(StatusPedido status) =>
        Mapa.TryGetValue(status, out var p)
            ? p
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Status fora do vocabulário canônico.");

    /// <summary>Rótulo para o lojista (Web/cockpit/Detail).</summary>
    public static string RotuloLojista(StatusPedido status) => De(status).RotuloLojista;

    /// <summary>Token do contrato do cliente storefront (PascalCase).</summary>
    public static string ContratoCliente(StatusPedido status) => De(status).ContratoCliente;

    /// <summary>Classificação operacional (pré-operacional / operacional / terminal).</summary>
    public static BucketStatusPedido Bucket(StatusPedido status) => De(status).Bucket;

    /// <summary>Atalho: o status é terminal (entregue/cancelado)?</summary>
    public static bool EhTerminal(StatusPedido status) => De(status).Bucket == BucketStatusPedido.Terminal;
}
