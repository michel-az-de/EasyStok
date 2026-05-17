using System.ComponentModel.DataAnnotations;

namespace EasyStock.Api.Models.Fiscal;

/// <summary>
/// Payload do <c>POST /api/notas-fiscais/emitir</c>. Caller (PWA Caixa, integrações) ja
/// deve ter o pedido fechado e com itens; este endpoint nao recalcula valores.
/// </summary>
public sealed class EmitirNfceRequest
{
    [Required]
    public Guid PedidoId { get; set; }

    /// <summary>UUID gerado pelo cliente. Reenvio com mesma chave retorna a mesma NFC-e sem duplicar.</summary>
    [Required]
    [StringLength(80, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = null!;

    [Range(0.01, double.MaxValue, ErrorMessage = "TotalNota deve ser maior que zero.")]
    public decimal TotalNota { get; set; }

    [Required]
    public EmitirNfceEmitenteDto Emitente { get; set; } = null!;

    public EmitirNfceDestinatarioDto? Destinatario { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Pelo menos um item e obrigatorio.")]
    public List<EmitirNfceItemDto> Itens { get; set; } = new();
}

public sealed class EmitirNfceEmitenteDto
{
    [Required][StringLength(14, MinimumLength = 14)]
    public string Cnpj { get; set; } = null!;

    [Required][StringLength(120)]
    public string RazaoSocial { get; set; } = null!;

    [StringLength(120)]
    public string? NomeFantasia { get; set; }

    [StringLength(20)]
    public string? InscricaoEstadual { get; set; }

    [StringLength(20)]
    public string? InscricaoMunicipal { get; set; }
}

public sealed class EmitirNfceDestinatarioDto
{
    [StringLength(14, MinimumLength = 11)]
    public string? CpfCnpj { get; set; }

    [StringLength(120)]
    public string? Nome { get; set; }

    [EmailAddress][StringLength(120)]
    public string? Email { get; set; }
}

public sealed class EmitirNfceItemDto
{
    [Required][StringLength(120)]
    public string NomeSnapshot { get; set; } = null!;

    [Range(0.0001, double.MaxValue)]
    public decimal Quantidade { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal PrecoUnitario { get; set; }

    [Required][StringLength(6)]
    public string Unidade { get; set; } = "UN";

    [StringLength(8, MinimumLength = 8)]
    public string? Ncm { get; set; }

    [StringLength(4, MinimumLength = 4)]
    public string? Cfop { get; set; }

    public Guid? ProdutoIdSnapshot { get; set; }

    [Range(0, 8)]
    public byte OrigemMercadoria { get; set; }

    [StringLength(10)]
    public string? CstOuCsosn { get; set; }
}
