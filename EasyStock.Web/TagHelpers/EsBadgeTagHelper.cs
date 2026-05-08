using EasyStock.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

[HtmlTargetElement("es-badge")]
public sealed class EsBadgeTagHelper : TagHelper
{
    private readonly LucideIconResolver _icons;

    public EsBadgeTagHelper(LucideIconResolver icons)
    {
        _icons = icons;
    }

    [HtmlAttributeName("variant")]
    public string Variant { get; set; } = "default";

    [HtmlAttributeName("icon")]
    public string? Icon { get; set; }

    [HtmlAttributeName("dot")]
    public bool Dot { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        var classes = new List<string> { "badge", VariantClass(Variant) };
        var existing = output.Attributes["class"]?.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(existing)) classes.Add(existing);
        output.Attributes.SetAttribute("class", string.Join(" ", classes));

        var inner = (await output.GetChildContentAsync()).GetContent();

        var html = new System.Text.StringBuilder();
        if (Dot) html.Append("<span class=\"badge-dot\" aria-hidden=\"true\"></span>");
        if (!string.IsNullOrWhiteSpace(Icon))
        {
            var svg = _icons.GetSvg(Icon);
            html.Append(svg ?? "<span class=\"es-icon es-icon--missing\" aria-hidden=\"true\"></span>");
        }
        if (!string.IsNullOrWhiteSpace(inner))
            html.Append("<span class=\"badge-label\">").Append(inner).Append("</span>");

        output.Content.SetHtmlContent(html.ToString());
    }

    private static string VariantClass(string variant) => variant?.ToLowerInvariant() switch
    {
        "primary" => "badge-primary",
        "navy" => "badge-navy",
        "accent" => "badge-accent",
        "ok" => "badge-ok",
        "success" => "badge-success",
        "warn" => "badge-warn",
        "crit" => "badge-crit",
        "danger" => "badge-danger",
        "info" => "badge-info",
        "neutral" => "badge-neutral",
        "soft" => "badge-soft",
        _ => "badge-default"
    };
}
