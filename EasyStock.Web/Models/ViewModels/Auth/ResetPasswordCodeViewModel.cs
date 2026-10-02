using System.ComponentModel.DataAnnotations;

namespace EasyStock.Web.Models.ViewModels.Auth;

/// <summary>Redefinição pelo código de 6 dígitos enviado por WhatsApp (N8).</summary>
public class ResetPasswordCodeViewModel
{
    [Required(ErrorMessage = "E-mail é obrigatório")]
    [EmailAddress(ErrorMessage = "E-mail inválido")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o código de 6 dígitos")]
    [RegularExpression("^[0-9]{6}$", ErrorMessage = "O código tem 6 dígitos")]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nova senha é obrigatória")]
    [MinLength(8, ErrorMessage = "Senha deve ter ao menos 8 caracteres")]
    [DataType(DataType.Password)]
    public string NovaSenha { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirmação de senha é obrigatória")]
    [DataType(DataType.Password)]
    [Compare(nameof(NovaSenha), ErrorMessage = "As senhas não coincidem")]
    public string ConfirmarSenha { get; set; } = string.Empty;
}
