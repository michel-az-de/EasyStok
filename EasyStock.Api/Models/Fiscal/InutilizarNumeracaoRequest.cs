namespace EasyStock.Api.Models.Fiscal;

/// <summary>
/// Payload do <c>POST /api/notas-fiscais/inutilizar</c>. Inutiliza faixa de
/// numeracao no mesmo ano fiscal apenas (SEFAZ nao aceita retroativo).
/// </summary>
public sealed class InutilizarNumeracaoRequest
{
    [Range(1, 999)]
    public short Serie { get; set; }

    [Range(1, 999_999_999)]
    public long NumeroInicial { get; set; }

    [Range(1, 999_999_999)]
    public long NumeroFinal { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 15, ErrorMessage = "Justificativa exige 15..255 caracteres (SEFAZ).")]
    public string Justificativa { get; set; } = null!;
}
