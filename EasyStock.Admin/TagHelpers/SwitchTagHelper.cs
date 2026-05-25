using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Switch (toggle visual) — semanticamente é um checkbox.
/// Uso: <es-switch name="ativo" label="Cupom ativo" checked="true" />
/// </summary>
[HtmlTargetElement("es-switch", TagStructure = TagStructure.WithoutEndTag)]
public sealed class SwitchTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Label { get; set; }
    public string? Value { get; set; } = "true";
    public bool Checked { get; set; }
    public bool Disabled { get; set; }
    public string? AriaLabel { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "label";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-switch");

        var id = Id ?? Name ?? $"es-sw-{Guid.NewGuid():N}".Substring(0, 12);

        var sb = new StringBuilder();
        sb.Append($"<input type=\"checkbox\" role=\"switch\" id=\"{WebUtility.HtmlEncode(id)}\"");
        if (!string.IsNullOrEmpty(Name)) sb.Append($" name=\"{WebUtility.HtmlEncode(Name)}\"");
        if (!string.IsNullOrEmpty(Value)) sb.Append($" value=\"{WebUtility.HtmlEncode(Value)}\"");
        if (Checked) sb.Append(" checked");
        if (Disabled) sb.Append(" disabled");
        if (string.IsNullOrEmpty(Label) && !string.IsNullOrEmpty(AriaLabel))
        {
            sb.Append($" aria-label=\"{WebUtility.HtmlEncode(AriaLabel)}\"");
        }
        sb.Append(" />");

        if (!string.IsNullOrWhiteSpace(Label))
        {
            sb.Append("<span>");
            sb.Append(WebUtility.HtmlEncode(Label));
            sb.Append("</span>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }
}
