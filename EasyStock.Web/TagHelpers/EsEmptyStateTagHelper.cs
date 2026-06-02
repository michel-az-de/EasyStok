using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

/// <summary>
/// Empty state — listagens vazias, onboarding, telas pos-acao.
/// Port do EasyStock.Admin/TagHelpers/EmptyStateTagHelper.cs (convergencia DS).
/// Diferenca: o icone vem do LucideIconResolver (inline SVG) em vez do sprite do
/// Admin — o Web nao tem sprite.svg (E5 do plano: paridade de icone).
///
/// Uso:
///   &lt;es-empty-state icon="inbox" title="Nenhum cliente" description="Comece criando."&gt;
///     &lt;es-button variant="primary"&gt;Novo&lt;/es-button&gt;
///   &lt;/es-empty-state&gt;
/// </summary>
[HtmlTargetElement("es-empty-state")]
public sealed class EsEmptyStateTagHelper : TagHelper
{
    private readonly LucideIconResolver _resolver;
    public EsEmptyStateTagHelper(LucideIconResolver resolver) => _resolver = resolver;

    public string Icon { get; set; } = "inbox";
    public string? Title { get; set; }
    public string? Description { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-empty-state");

        var content = await output.GetChildContentAsync();
        var actions = content.GetContent() ?? string.Empty;

        var sb = new StringBuilder();
        sb.Append("<div class=\"es-empty-state-icon\">");
        var svg = _resolver.GetSvg(Icon);
        if (svg is not null)
            sb.Append(Regex.Replace(svg, "<svg\\b", "<svg class=\"es-icon\" aria-hidden=\"true\" focusable=\"false\"", RegexOptions.IgnoreCase));
        else
            sb.Append("<span class=\"es-icon es-icon--missing\" aria-hidden=\"true\"></span>");
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(Title))
            sb.Append("<div class=\"es-empty-state-title\">").Append(WebUtility.HtmlEncode(Title)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(Description))
            sb.Append("<div class=\"es-empty-state-desc\">").Append(WebUtility.HtmlEncode(Description)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(actions))
            sb.Append("<div class=\"es-empty-state-actions\">").Append(actions).Append("</div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}
