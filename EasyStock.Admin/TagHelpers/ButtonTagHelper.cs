using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Botão (ou link com aparência de botão) com variantes semânticas e estado de loading.
///
/// Uso:
///   <es-button>Salvar</es-button>
///   <es-button variant="danger" icon="trash-2">Excluir</es-button>
///   <es-button variant="secondary" href="/Tenants">Voltar</es-button>
///   <es-button type="submit" loading="true">Enviando…</es-button>
///   <es-button icon="more-horizontal" icon-only="true" aria-label="Mais ações" variant="ghost"></es-button>
/// </summary>
[HtmlTargetElement("es-button")]
public sealed class ButtonTagHelper : TagHelper
{
    public string Variant { get; set; } = "primary";
    public string Size { get; set; } = "md";
    public string? Icon { get; set; }

    [HtmlAttributeName("icon-trailing")]
    public string? IconTrailing { get; set; }

    public bool Loading { get; set; }
    public bool Block { get; set; }

    [HtmlAttributeName("icon-only")]
    public bool IconOnly { get; set; }

    public string? Href { get; set; }
    public string Type { get; set; } = "button";
    public string? Target { get; set; }
    public string? Formaction { get; set; }
    public bool Disabled { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var isLink = !string.IsNullOrEmpty(Href);
        output.TagName = isLink ? "a" : "button";
        output.TagMode = TagMode.StartTagAndEndTag;

        var classes = new List<string> { "btn", $"btn-{Variant}" };
        if (Size != "md") classes.Add($"btn-{Size}");
        else classes.Add("btn-md");
        if (Block) classes.Add("btn-block");
        if (Loading) classes.Add("is-loading");
        if (IconOnly) classes.Add("btn-icon");

        output.Attributes.SetAttribute("class", string.Join(" ", classes));

        if (isLink)
        {
            output.Attributes.SetAttribute("href", Href!);
            if (!string.IsNullOrEmpty(Target))
            {
                output.Attributes.SetAttribute("target", Target);
                if (Target == "_blank")
                {
                    output.Attributes.SetAttribute("rel", "noopener noreferrer");
                }
            }
            if (Disabled || Loading)
            {
                output.Attributes.SetAttribute("aria-disabled", "true");
                output.Attributes.SetAttribute("tabindex", "-1");
            }
        }
        else
        {
            output.Attributes.SetAttribute("type", Type);
            if (Disabled || Loading) output.Attributes.SetAttribute("disabled", "disabled");
            if (Loading) output.Attributes.SetAttribute("aria-busy", "true");
            if (!string.IsNullOrEmpty(Formaction)) output.Attributes.SetAttribute("formaction", Formaction);
        }

        var content = await output.GetChildContentAsync();
        var label = content.GetContent() ?? string.Empty;

        var sb = new StringBuilder();
        if (Loading)
        {
            sb.Append("<span class=\"es-spinner\" aria-hidden=\"true\"></span>");
        }
        if (!string.IsNullOrWhiteSpace(Icon))
        {
            sb.Append(BuildIcon(Icon, IconSize));
        }
        if (!IconOnly)
        {
            sb.Append("<span>");
            sb.Append(label);
            sb.Append("</span>");
        }
        if (!string.IsNullOrWhiteSpace(IconTrailing))
        {
            sb.Append(BuildIcon(IconTrailing, IconSize));
        }

        output.Content.SetHtmlContent(sb.ToString());
    }

    private int IconSize => Size switch
    {
        "xs" => 12,
        "sm" => 14,
        "lg" => 18,
        _ => 16
    };

    private static string BuildIcon(string name, int size)
    {
        var safe = WebUtility.HtmlEncode(name);
        return $"<svg class=\"es-icon\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-{safe}\"/></svg>";
    }
}
