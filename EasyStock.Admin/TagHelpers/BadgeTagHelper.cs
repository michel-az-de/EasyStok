using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Badge / pill com variantes semânticas.
///
/// Uso:
///   <es-badge variant="ok">ATIVA</es-badge>
///   <es-badge variant="crit" with-dot="true">EXPIRADA</es-badge>
///   <es-badge variant="info" icon="check">PAGA</es-badge>
/// </summary>
[HtmlTargetElement("es-badge")]
public sealed class BadgeTagHelper : TagHelper
{
    public string Variant { get; set; } = "neutral";

    [HtmlAttributeName("with-dot")]
    public bool WithDot { get; set; }

    public string? Icon { get; set; }
    public string? Title { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"badge badge-{Variant}");
        if (!string.IsNullOrEmpty(Title)) output.Attributes.SetAttribute("title", Title);

        var content = await output.GetChildContentAsync();
        var label = content.GetContent() ?? string.Empty;

        var sb = new StringBuilder();
        if (WithDot)
        {
            sb.Append("<span class=\"dot\" aria-hidden=\"true\"></span>");
        }
        if (!string.IsNullOrWhiteSpace(Icon))
        {
            var safe = WebUtility.HtmlEncode(Icon);
            sb.Append($"<svg class=\"es-icon\" width=\"12\" height=\"12\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-{safe}\"/></svg>");
        }
        sb.Append(label);

        output.Content.SetHtmlContent(sb.ToString());
    }
}
