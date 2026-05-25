using System.ComponentModel.DataAnnotations;

namespace EasyStock.Web.Models.ViewModels.Onboarding;

public class OnboardingViewModel
{
    [Display(Name = "Nome fantasia")]
    [StringLength(150)]
    public string? NomeFantasia { get; set; }

    [Display(Name = "Telefone / WhatsApp")]
    [StringLength(30)]
    public string? Telefone { get; set; }

    [Required(ErrorMessage = "Selecione o segmento.")]
    [Display(Name = "Segmento")]
    public string Segmento { get; set; } = "outro";

    [Required(ErrorMessage = "Nome da loja e obrigatorio.")]
    [Display(Name = "Nome da loja")]
    [StringLength(150)]
    public string LojaNome { get; set; } = string.Empty;

    [Display(Name = "Endereco / cidade")]
    [StringLength(300)]
    public string? LojaEndereco { get; set; }

    [Display(Name = "Telefone da loja")]
    [StringLength(30)]
    public string? LojaTelefone { get; set; }
}
