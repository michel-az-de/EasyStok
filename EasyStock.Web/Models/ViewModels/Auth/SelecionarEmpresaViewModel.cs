using EasyStock.Web.Services;

namespace EasyStock.Web.Models.ViewModels.Auth;

/// <summary>
/// Passo 2 do login em duas etapas (ADR-0047): o usuario escolhe entre as empresas que
/// acessa. Carrega apenas o e-mail e a lista — a senha da pendencia nunca chega a view.
/// </summary>
public sealed class SelecionarEmpresaViewModel
{
    public string Email { get; init; } = string.Empty;
    public IReadOnlyList<EmpresaLoginItem> Empresas { get; init; } = [];
}
