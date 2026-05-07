using System.ComponentModel.DataAnnotations;

namespace EasyStock.Web.Models.ViewModels.Site;

public sealed class ContatoViewModel
{
    [Required(ErrorMessage = "Como podemos te chamar?")]
    [StringLength(150)]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "Precisamos do seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail invalido.")]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [StringLength(32)]
    public string? Telefone { get; set; }

    [StringLength(150)]
    public string? Empresa { get; set; }

    [StringLength(80)]
    public string? TipoNegocio { get; set; }

    [Required(ErrorMessage = "Conta pra gente o que precisa.")]
    [StringLength(2000, MinimumLength = 5, ErrorMessage = "Mensagem muito curta.")]
    public string Mensagem { get; set; } = string.Empty;

    [Display(Name = "Concordo com a Politica de Privacidade")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "Voce precisa aceitar para enviar.")]
    public bool ConsentimentoLgpd { get; set; }

    public bool ReceberNewsletter { get; set; }

    /// <summary>Honeypot — bots preenchem; humanos nao veem.</summary>
    public string? Website { get; set; }

    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
}

public sealed class NewsletterViewModel
{
    [Required(ErrorMessage = "Precisamos do seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail invalido.")]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    public string? Nome { get; set; }
    public bool ConsentimentoLgpd { get; set; } = true;
    public string? Website { get; set; }
}
