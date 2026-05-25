using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Empty state — usado em listagens vazias, onboarding e telas pós-ação.
///
/// Uso:
///   <es-empty-state icon="inbox"
///                   title="Nenhum cliente"
///                   description="Comece criando seu primeiro cliente.">
///     <es-button variant="primary" icon="plus">Novo cliente</es-button>
///   </es-empty-state>
/// </summary>
[HtmlTargetElement("es-empty-state")]
public sealed class EmptyStateTagHelper : TagHelper
{
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
        var safeIcon = WebUtility.HtmlEncode(Icon);
        sb.Append("<div class=\"es-empty-state-icon\">");
        sb.Append($"<svg class=\"es-icon\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-{safeIcon}\"/></svg>");
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(Title))
        {
            sb.Append("<div class=\"es-empty-state-title\">");
            sb.Append(WebUtility.HtmlEncode(Title));
            sb.Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(Description))
        {
            sb.Append("<div class=\"es-empty-state-desc\">");
            sb.Append(WebUtility.HtmlEncode(Description));
            sb.Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(actions))
        {
            sb.Append("<div class=\"es-empty-state-actions\">");
            sb.Append(actions);
            sb.Append("</div>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }
}
