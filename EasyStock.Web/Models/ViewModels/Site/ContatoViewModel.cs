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
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Mensagem muito curta — escreve pelo menos 10 caracteres.")]
    public string Mensagem { get; set; } = string.Empty;

    [Display(Name = "Concordo com a Politica de Privacidade")]
    [MustBeTrue(ErrorMessage = "Voce precisa aceitar a Politica de Privacidade para enviar.")]
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

    /// <summary>
    /// Default true — newsletter inscricao implica consentimento LGPD basico.
    /// Esquema mais explicito (checkbox dedicado) fica para um banner LGPD em P1.
    /// </summary>
    public bool ConsentimentoLgpd { get; set; } = true;

    public string? Website { get; set; }
}

/// <summary>
/// Valida que um campo bool e <c>true</c>. Substitui o <c>[Range(typeof(bool),
/// "true", "true")]</c> que e fragil em .NET 9 — RangeAttribute usa
/// <c>Convert.ChangeType</c> + <c>IComparable</c>, mas <c>bool</c> nao
/// implementa <c>IComparable&lt;bool&gt;</c>, podendo lancar runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is true;
}
