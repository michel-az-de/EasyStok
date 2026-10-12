namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Lançamento manual de caixa. Pode ser despesa (compra de insumo) ou
/// entrada extra (pagamento por fora). Vendas de pedidos NÃO geram CashEntry —
/// são totalizadas via <c>Order.Status = "entregue"</c>.
/// #1520 (ADR-0060): edição e exclusão feitas no PWA chegam aqui e ao
/// <see cref="EasyStock.Domain.Entities.MovimentoCaixa"/> vinculado.
/// </summary>
[Table("mobile_cash_entries")]
public class CashEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>"expense" ou "income".</summary>
    [Required, MaxLength(16)]
    public string Type { get; set; } = "expense";

    [Column(TypeName = "numeric(10,2)")]
    public decimal Amount { get; set; }

    [Required, MaxLength(255)]
    public string Description { get; set; } = default!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// #1520 (ADR-0060) — carimbo do relógio do SERVIDOR, gravado em toda inserção e alteração
    /// pelo <c>AuditTimestampsInterceptor</c>. Nunca vem do aparelho. É o filtro e o cursor do
    /// pull e a base da detecção de conflito; <see cref="CreatedAt"/> traz a hora do aparelho.
    /// </summary>
    [Column("server_updated_at")]
    public DateTime ServerUpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_device_id"), MaxLength(64)]
    public string? LastDeviceId { get; set; }

    [Column("last_operator_name"), MaxLength(64)]
    public string? LastOperatorName { get; set; }

    /// <summary>Multi-tenant (Onda 1). Resolvido via device autenticado.</summary>
    [Column("empresa_id")]
    public Guid? EmpresaId { get; set; }

    [Column("loja_id")]
    public Guid? LojaId { get; set; }

    /// <summary>
    /// Onda P3 — link pra <see cref="EasyStock.Domain.Entities.MovimentoCaixa"/>
    /// no ERP. NULL = ainda não promovido pro ERP. Quando linkado, o
    /// fechamento de caixa do ERP enxerga esse movimento.
    /// </summary>
    [Column("erp_movimento_caixa_id")]
    public Guid? ErpMovimentoCaixaId { get; set; }

    /// <summary>
    /// #1493 — forma de pagamento informada no PWA
    /// ("pix" | "dinheiro" | "credito" | "debito" | "transferencia" | "outro").
    /// NULL = aparelho antigo que nao pergunta; o movimento no ERP cai em "dinheiro".
    /// </summary>
    [Column("metodo"), MaxLength(20)]
    public string? Metodo { get; set; }

    /// <summary>
    /// #1520 — exclusão feita pelo operador no PWA ("cashEntry.delete"). A linha fica; no ERP o
    /// movimento vinculado é estornado. Nunca é preenchido por ausência do lançamento no aparelho.
    /// </summary>
    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [Column("deleted_by"), MaxLength(64)]
    public string? DeletedBy { get; set; }
}
