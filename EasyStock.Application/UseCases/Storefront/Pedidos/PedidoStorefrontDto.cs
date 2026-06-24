namespace EasyStock.Application.UseCases.Storefront.Pedidos;

/// <summary>
/// DTO público de um pedido na listagem <c>GET /api/storefront/{slug}/pedidos</c>.
/// Shape estável definido em <c>docs/multi-agent/contracts/listar-pedidos-cliente.contract.md</c>.
///
/// <para>
/// Centavos como <see cref="long"/> (mesma convenção de
/// <c>menu-publico</c> / <c>iniciar-checkout</c>) para evitar floating-point.
/// </para>
/// </summary>
/// <param name="PedidoId">PK do pedido (Guid).</param>
/// <param name="CriadoEm">Timestamp UTC ISO-8601.</param>
/// <param name="Status">String canônica do contrato (PascalCase) — ex: "AguardandoPagamento".</param>
/// <param name="Itens">Snapshot dos itens (nome + qtd + preço unitário no momento da compra).</param>
/// <param name="SubtotalCentavos">Soma dos itens com ProdutoId não-nulo.</param>
/// <param name="FreteCentavos">Item de frete extraído (PedidoItem onde ProdutoId == null e Nome começa com "Entrega").</param>
/// <param name="TotalCentavos"><c>Pedido.Total</c> em centavos. Subtotal + frete.</param>
/// <param name="JanelaEntrega">Detalhes da janela escolhida (via VagaOcupada). Null se vaga já liberada e sem histórico.</param>
/// <param name="Endereco">Endereço de entrega. Null no MVP — backend ainda não snapshota endereço no checkout (ver task TASK-EZ-PEDIDOS-002).</param>
/// <param name="Avaliacao">Avaliação do cliente (se Entregue + avaliado). Null caso contrário.</param>
/// <param name="InitPointUrl">URL do MercadoPago para retomar pagamento. Null no MVP — preference URL não está persistida (ver task TASK-EZ-PEDIDOS-003).</param>
/// <param name="MotivoCancelamento">Mensagem ao cliente quando status == Cancelado/Recusado. Null caso contrário.</param>
/// <param name="Pagamento">Pagamento confirmado (método + timestamp). Null se ainda sem pagamento.</param>
public sealed record PedidoStorefrontDto(
    Guid PedidoId,
    DateTime CriadoEm,
    string Status,
    IReadOnlyList<PedidoStorefrontItemDto> Itens,
    long SubtotalCentavos,
    long FreteCentavos,
    long TotalCentavos,
    PedidoStorefrontJanelaDto? JanelaEntrega,
    PedidoStorefrontEnderecoDto? Endereco,
    PedidoStorefrontAvaliacaoDto? Avaliacao,
    string? InitPointUrl,
    string? MotivoCancelamento,
    PedidoStorefrontPagamentoDto? Pagamento);

/// <summary>Item snapshotado do pedido (nome do produto no momento + qtd × preço unitário).</summary>
/// <param name="Nome">Snapshot do nome no momento da compra.</param>
/// <param name="Qtd">Quantidade (arredondada pra inteiro).</param>
/// <param name="PrecoCentavos">Preço unitário em centavos.</param>
/// <param name="Descricao">Rótulo da variação/opção escolhida (ex: "tamanho família"). Null se item sem variação.</param>
public sealed record PedidoStorefrontItemDto(
    string Nome,
    int Qtd,
    long PrecoCentavos,
    string? Descricao);

/// <summary>Janela de entrega escolhida — copiada da <c>JanelaEntrega</c> referenciada via <c>VagaOcupada</c>.</summary>
/// <param name="Data">Data da entrega (yyyy-MM-dd).</param>
/// <param name="HoraInicio">"HH:mm" — sem segundos pra simplificar formatação no frontend.</param>
/// <param name="HoraFim">"HH:mm".</param>
/// <param name="Label">Rótulo público (ex: "Sábado 12h–14h").</param>
public sealed record PedidoStorefrontJanelaDto(
    DateOnly Data,
    string HoraInicio,
    string HoraFim,
    string Label);

/// <summary>Endereço de entrega — snapshot ou fallback do cliente. Sempre null no MVP.</summary>
public sealed record PedidoStorefrontEnderecoDto(
    string? Rua,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Cep);

/// <summary>Avaliação do cliente — preenchida só quando o cliente respondeu pós-entrega.</summary>
public sealed record PedidoStorefrontAvaliacaoDto(
    int Estrelas,
    string? Comentario);

/// <summary>Pagamento confirmado — o mais antigo de <c>Pedido.Pagamentos</c>. Null se ainda sem pagamento.</summary>
/// <param name="Metodo">"pix" | "dinheiro" | "credito" | "debito" | "transferencia" | "outro".</param>
/// <param name="ConfirmadoEm">Timestamp UTC do pagamento (<c>PedidoPagamento.PagoEm</c>).</param>
public sealed record PedidoStorefrontPagamentoDto(
    string Metodo,
    DateTime ConfirmadoEm);
