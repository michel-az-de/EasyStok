using System.ComponentModel.DataAnnotations;

namespace EasyStock.Api.Models.Fiscal;

public sealed class ConfigurarCscRequest
{
    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string CscId { get; set; } = null!;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CscToken { get; set; } = null!;
}
