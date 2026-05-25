using System.ComponentModel.DataAnnotations;

namespace EasyStock.Web.Models.ViewModels.Auth;

public class LoginViewModel
{
    [Required(ErrorMessage = "E-mail é obrigatório")]
    [EmailAddress(ErrorMessage = "E-mail inválido")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Senha é obrigatória")]
    [DataType(DataType.Password)]
    public string Senha { get; set; } = string.Empty;

    public bool ManterLogado { get; set; }
}
