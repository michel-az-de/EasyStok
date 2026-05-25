using System.ComponentModel.DataAnnotations;

namespace EasyStock.Api.Models.Fiscal;

/// <summary>
/// Payload do <c>POST /api/notas-fiscais/{id}/cancelar</c>. SEFAZ exige minimo 15
/// caracteres no motivo e nota deve estar no prazo (24h padrao).
/// </summary>
public sealed class CancelarNfeRequest
{
    [Required]
    [StringLength(255, MinimumLength = 15, ErrorMessage = "Motivo exige 15..255 caracteres (SEFAZ).")]
    public string Motivo { get; set; } = null!;
}
