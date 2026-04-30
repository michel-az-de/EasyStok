namespace EasyStock.Application.UseCases.Pedidos;

/// <summary>DTO de retorno de Use cases de Pedido (Onda P2).</summary>
public sealed record PedidoResult(
    Guid Id,
    Guid EmpresaId,
    Guid? LojaId,
    Guid? ClienteId,
    string? ClienteNome,
    string? ClienteApt,
    string? ClienteTelefone,
    string Status,
    decimal Total,
    decimal TotalPago,
    string? Observacoes,
    string? Origem,
    string? MobileOrderId,
    Guid? VendaId,
    int ItensCount,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    DateTime? EntreguEm,
    DateTime? CanceladoEm
);

public sealed record PedidoDetalheResult(
    PedidoResult Pedido,
    IReadOnlyList<PedidoItemResult> Itens,
    IReadOnlyList<PedidoEventoResult> Eventos,
    IReadOnlyList<PedidoPagamentoResult> Pagamentos
);

public sealed record PedidoItemResult(
    Guid Id, Guid PedidoId, Guid? ProdutoId,
    string Nome, string? Emoji, string? Unidade,
    decimal Quantidade, decimal PrecoUnitario, decimal Subtotal,
    string? Observacao, DateTime CriadoEm
);

public sealed record PedidoEventoResult(
    Guid Id, Guid PedidoId, string Tipo,
    string? StatusAntigo, string? StatusNovo,
    string? Detalhes, Guid? UsuarioId, string? UsuarioNome,
    string? Origem, DateTime OcorridoEm
);

public sealed record PedidoPagamentoResult(
    Guid Id, Guid PedidoId, string Metodo, decimal Valor,
    string? Referencia, string? Observacao,
    DateTime PagoEm, Guid? RegistradoPorUserId, string? RegistradoPorNome
);
