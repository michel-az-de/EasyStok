namespace EasyStock.Web.Models.Api;

public record Pedido
{
    public required string Id { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public Guid? ClienteId { get; init; }
    public string? ClienteNome { get; init; }
    public string? ClienteApt { get; init; }
    public string? ClienteTelefone { get; init; }
    public required string Status { get; init; }
    public decimal Total { get; init; }
    public decimal TotalPago { get; init; }
    public string? Observacoes { get; init; }
    public string? Origem { get; init; }
    public string? MobileOrderId { get; init; }
    public Guid? VendaId { get; init; }
    public int ItensCount { get; init; }
    public DateTime CriadoEm { get; init; }
    public DateTime AlteradoEm { get; init; }
    public DateTime? EntreguEm { get; init; }
    public DateTime? CanceladoEm { get; init; }

    public decimal Pendente => Math.Max(0m, Total - TotalPago);
    public bool Quitado => TotalPago >= Total && Total > 0;
}

public record PedidoDetalhe
{
    public required Pedido Pedido { get; init; }
    public List<PedidoItemDto> Itens { get; init; } = new();
    public List<PedidoEventoDto> Eventos { get; init; } = new();
    public List<PedidoPagamentoDto> Pagamentos { get; init; } = new();
}

public record PedidoItemDto(
    string Id, string PedidoId, Guid? ProdutoId,
    string Nome, string? Emoji, string? Unidade,
    decimal Quantidade, decimal PrecoUnitario, decimal Subtotal,
    string? Observacao, DateTime CriadoEm);

public record PedidoEventoDto(
    string Id, string PedidoId, string Tipo,
    string? StatusAntigo, string? StatusNovo,
    string? Detalhes, Guid? UsuarioId, string? UsuarioNome,
    string? Origem, DateTime OcorridoEm);

public record PedidoPagamentoDto(
    string Id, string PedidoId, string Metodo, decimal Valor,
    string? Referencia, string? Observacao,
    DateTime PagoEm, Guid? RegistradoPorUserId, string? RegistradoPorNome);

public record MobilePedidoSummary
{
    public required string Id { get; init; }
    public string? ClientId { get; init; }
    public required string ClientSnapshotName { get; init; }
    public string? ClientSnapshotRef { get; init; }
    public string? Notes { get; init; }
    public decimal Total { get; init; }
    public required string Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid? EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public Guid? ErpPedidoId { get; init; }
    public Guid? ErpVendaId { get; init; }
    public string? LastDeviceId { get; init; }
    public string? LastOperatorName { get; init; }
    public bool Linked => ErpPedidoId.HasValue && ErpPedidoId.Value != Guid.Empty;
    // F5 — agendamento (MVP). NULL = pedido pra agora.
    public DateTime? ScheduledDeliveryAt { get; init; }
    public bool IsScheduled => ScheduledDeliveryAt.HasValue;
    public bool IsAtrasado => ScheduledDeliveryAt.HasValue
        && ScheduledDeliveryAt.Value < DateTime.UtcNow
        && Status != "entregue" && Status != "cancelado";
}
