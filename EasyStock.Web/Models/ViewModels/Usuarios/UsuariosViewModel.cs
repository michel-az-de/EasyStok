using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Usuarios;

public class UsuariosViewModel
{
    public List<UsuarioInfo> Usuarios { get; set; } = [];
    public List<Loja> Lojas { get; set; } = [];
    public int TotalAdmins { get; set; }
    public int TotalColaboradores { get; set; }

    public static readonly (string Value, string Label, string BadgeClass)[] PerfisDisponiveis =
    [
        ("Admin", "Administrador", "bg-indigo-100 text-indigo-700"),
        ("Gerente", "Gerente", "bg-amber-100 text-amber-700"),
        ("Operador", "Operador", "bg-slate-100 text-slate-600"),
    ];

    public static (string Label, string BadgeClass) GetPerfilDisplay(string? role) => role switch
    {
        "SuperAdmin" => ("Super Admin", "bg-indigo-100 text-indigo-700"),
        "Admin" => ("Administrador", "bg-indigo-100 text-indigo-700"),
        "Gerente" => ("Gerente", "bg-amber-100 text-amber-700"),
        _ => ("Operador", "bg-slate-100 text-slate-600"),
    };
}

public class UsuarioInfo
{
    public string Id { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? LojaId { get; set; }

    /// <summary>N9: <c>pendente</c>, <c>aceito</c> ou <c>nenhum</c>.</summary>
    public string ConviteEstado { get; set; } = "nenhum";

    /// <summary>N9: por onde o convite foi aceito, já mascarado pela API (<c>+55•••1234</c>, <c>e-mail</c>, <c>Google</c>).</summary>
    public string? ConviteVia { get; set; }
}

public class ConvidarUsuarioViewModel
{
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid? PerfilId { get; set; }
    public Guid? LojaId { get; set; }

    /// <summary>Opcional (N9). Só vira canal do convite com <see cref="AtestaOptInWhatsApp"/>.</summary>
    public string? Telefone { get; set; }

    /// <summary>A dona atesta que a pessoa aceitou receber o convite por WhatsApp.</summary>
    public bool AtestaOptInWhatsApp { get; set; }
}
