using System.ComponentModel.DataAnnotations;

namespace EasyStock.Web.Models.ViewModels.Auth;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Nome da empresa é obrigatório")]
    [MaxLength(150)]
    public string NomeEmpresa { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Documento { get; set; }

    [Required(ErrorMessage = "Seu nome é obrigatório")]
    [MaxLength(100)]
    public string NomeAdmin { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-mail é obrigatório")]
    [EmailAddress(ErrorMessage = "E-mail inválido")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Senha é obrigatória")]
    [MinLength(8, ErrorMessage = "Senha deve ter pelo menos 8 caracteres")]
    [DataType(DataType.Password)]
    public string Senha { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirmação de senha é obrigatória")]
    [Compare(nameof(Senha), ErrorMessage = "Senhas não conferem")]
    [DataType(DataType.Password)]
    public string ConfirmarSenha { get; set; } = string.Empty;
}
