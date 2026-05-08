using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Checkbox custom (mantém input nativo, só estiliza).
/// Uso:
///   <es-checkbox name="aceite" label="Aceito os termos" required="true" />
///   <es-checkbox name="ativos" checked="true" label="Apenas ativos" helper="Status" />
/// </summary>
[HtmlTargetElement("es-checkbox", TagStructure = TagStructure.WithoutEndTag)]
public sealed class CheckboxTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Label { get; set; }
    public string? Helper { get; set; }
    public string? Value { get; set; } = "true";
    public bool Checked { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "label";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-checkbox");

        var id = Id ?? Name ?? $"es-cb-{Guid.NewGuid():N}".Substring(0, 12);

        var sb = new StringBuilder();
        sb.Append($"<input type=\"checkbox\" id=\"{WebUtility.HtmlEncode(id)}\"");
        if (!string.IsNullOrEmpty(Name)) sb.Append($" name=\"{WebUtility.HtmlEncode(Name)}\"");
        if (!string.IsNullOrEmpty(Value)) sb.Append($" value=\"{WebUtility.HtmlEncode(Value)}\"");
        if (Checked) sb.Append(" checked");
        if (Required) sb.Append(" required");
        if (Disabled) sb.Append(" disabled");
        sb.Append(" />");

        sb.Append("<span>");
        if (!string.IsNullOrWhiteSpace(Label))
        {
            sb.Append($"<span class=\"es-checkbox-label\">{WebUtility.HtmlEncode(Label)}</span>");
        }
        if (!string.IsNullOrWhiteSpace(Helper))
        {
            sb.Append($"<span class=\"es-checkbox-helper\">{WebUtility.HtmlEncode(Helper)}</span>");
        }
        sb.Append("</span>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}
