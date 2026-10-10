namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Pagamento como o Mercado Pago o devolve em <c>GET v1/payments/{id}</c> (S32), só com os campos que o
/// EasyStok usa. <see cref="ExternalReference"/> é o <c>PedidoId</c> gravado na preferência.
/// </summary>
public sealed record PagamentoMercadoPago(
    string Id,
    string Status,
    string? StatusDetail,
    string? ExternalReference,
    decimal TransactionAmount,
    DateTime? DateApproved,
    string? PaymentMethodId,
    string? PaymentTypeId,
    decimal TransactionAmountRefunded = 0)
{
    public const string Approved = "approved";
    public const string Pending = "pending";
    public const string InProcess = "in_process";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Refunded = "refunded";
    public const string ChargedBack = "charged_back";

    public bool Aprovado => string.Equals(Status, Approved, StringComparison.OrdinalIgnoreCase);
    public bool Estornado => EhEstorno(Status);
    public bool Recusado => EhRecusa(Status);

    /// <summary><c>refunded</c> ou <c>charged_back</c>: o dinheiro voltou ao comprador.</summary>
    public static bool EhEstorno(string? status) =>
        string.Equals(status, Refunded, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, ChargedBack, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>rejected</c> ou <c>cancelled</c>: o pagamento não aconteceu.</summary>
    public static bool EhRecusa(string? status) =>
        string.Equals(status, Rejected, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Estorno criado pelo Mercado Pago (<c>POST v1/payments/{id}/refunds</c>).</summary>
public sealed record EstornoMercadoPagoResult(string EstornoId, decimal? Valor, string? Status,
    string? PagamentoId = null, DateTime? CriadoEm = null);
