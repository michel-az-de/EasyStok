using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

/// <summary>
/// TagHelper cosmetico para esconder elementos da UI quando o perfil atual nao tem
/// permissao. NAO substitui [Authorize] no controller. Uso:
///
///   <authorize-can role="Admin,Owner">...</authorize-can>
///   <authorize-can role-not="Visualizador">...</authorize-can>
///
/// O perfil ativo vem de ViewBag.Role (preenchido em BaseController.cs).
/// </summary>
[HtmlTargetElement("authorize-can")]
public sealed class AuthorizeCanTagHelper : TagHelper
{
    [HtmlAttributeName("role")]
    public string? Role { get; set; }

    [HtmlAttributeName("role-not")]
    public string? RoleNot { get; set; }

    [HtmlAttributeName("when")]
    public bool? When { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext? ViewContext { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        var allow = true;

        if (When.HasValue)
            allow = allow && When.Value;

        var current = (ViewContext?.ViewBag.Role as string)?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(Role))
        {
            var roles = Role.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            allow = allow && roles.Any(r => string.Equals(r, current, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(RoleNot))
        {
            var rolesNot = RoleNot.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            allow = allow && !rolesNot.Any(r => string.Equals(r, current, StringComparison.OrdinalIgnoreCase));
        }

        if (!allow)
            output.SuppressOutput();
    }
}
