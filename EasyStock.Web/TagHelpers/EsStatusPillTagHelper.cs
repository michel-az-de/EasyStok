using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

[HtmlTargetElement("es-status-pill")]
public sealed class EsStatusPillTagHelper : TagHelper
{
    [HtmlAttributeName("status")]
    public string Status { get; set; } = "neutral";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        var classes = new List<string> { "stp", StatusClass(Status) };
        var existing = output.Attributes["class"]?.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(existing)) classes.Add(existing);
        output.Attributes.SetAttribute("class", string.Join(" ", classes));

        var inner = (await output.GetChildContentAsync()).GetContent();
        output.Content.SetHtmlContent(
            $"<span class=\"stp-dot\" aria-hidden=\"true\"></span><span class=\"stp-label\">{inner}</span>");
    }

    private static string StatusClass(string status) => status?.ToLowerInvariant() switch
    {
        "ok" => "stp-ok",
        "warn" => "stp-warn",
        "crit" => "stp-crit",
        "info" => "stp-info",
        "accent" => "stp-accent",
        "ink" => "stp-ink",
        "neutral" => "stp-neutral",
        _ => "stp-neutral"
    };
}
