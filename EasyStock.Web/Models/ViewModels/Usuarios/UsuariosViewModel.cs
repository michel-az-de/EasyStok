using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Usuarios;

public class UsuariosViewModel
{
    public List<UsuarioInfo> Usuarios { get; set; } = [];
    public List<Loja> Lojas { get; set; } = [];
    public int TotalAdmins { get; set; }
    public int TotalColaboradores { get; set; }
}

public class UsuarioInfo
{
    public string Id { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? LojaId { get; set; }
}

public class ConvidarUsuarioViewModel
{
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
}
