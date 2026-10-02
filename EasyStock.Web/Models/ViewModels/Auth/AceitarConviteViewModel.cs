using System.ComponentModel.DataAnnotations;

namespace EasyStock.Web.Models.ViewModels.Auth;

/// <summary>
/// Aceite do convite de primeiro acesso (N9). O <see cref="Token"/> vem do link (query ou fragmento) e só é enviado à API no
/// POST: abrir a tela nunca o consome. Sem token a pessoa recebe a mesma mensagem de convite inválido.
/// </summary>
public class AceitarConviteViewModel
{
    [Required(ErrorMessage = "Convite inválido ou expirado.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nova senha é obrigatória")]
    [MinLength(8, ErrorMessage = "Senha deve ter ao menos 8 caracteres")]
    [DataType(DataType.Password)]
    public string NovaSenha { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirmação de senha é obrigatória")]
    [DataType(DataType.Password)]
    [Compare(nameof(NovaSenha), ErrorMessage = "As senhas não coincidem")]
    public string ConfirmarSenha { get; set; } = string.Empty;
}
