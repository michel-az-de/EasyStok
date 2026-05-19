using EasyStock.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

[HtmlTargetElement("es-button")]
public sealed class EsButtonTagHelper : TagHelper
{
    private readonly LucideIconResolver _icons;

    public EsButtonTagHelper(LucideIconResolver icons)
    {
        _icons = icons;
    }

    [HtmlAttributeName("variant")]
    public string Variant { get; set; } = "primary";

    [HtmlAttributeName("size")]
    public string Size { get; set; } = "md";

    [HtmlAttributeName("icon")]
    public string? Icon { get; set; }

    [HtmlAttributeName("icon-trailing")]
    public string? IconTrailing { get; set; }

    [HtmlAttributeName("icon-only")]
    public bool IconOnly { get; set; }

    [HtmlAttributeName("loading")]
    public bool Loading { get; set; }

    [HtmlAttributeName("type")]
    public string Type { get; set; } = "button";

    [HtmlAttributeName("href")]
    public string? Href { get; set; }

    [HtmlAttributeName("disabled")]
    public bool Disabled { get; set; }

    [HtmlAttributeName("aria-label")]
    public string? AriaLabel { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var asLink = !string.IsNullOrWhiteSpace(Href);
        output.TagName = asLink ? "a" : "button";
        output.TagMode = TagMode.StartTagAndEndTag;

        var classes = new List<string> { "btn", VariantClass(Variant) };
        var sizeClass = SizeClass(Size);
        if (!string.IsNullOrEmpty(sizeClass)) classes.Add(sizeClass);
        if (IconOnly) classes.Add("btn-icon");

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(existingClass))
            classes.Add(existingClass);
        output.Attributes.SetAttribute("class", string.Join(" ", classes));

        if (asLink)
        {
            output.Attributes.SetAttribute("href", Href!);
            if (Disabled)
            {
                output.Attributes.SetAttribute("aria-disabled", "true");
                output.Attributes.SetAttribute("tabindex", "-1");
            }
        }
        else
        {
            output.Attributes.SetAttribute("type", Type);
            if (Disabled) output.Attributes.SetAttribute("disabled", "disabled");
        }

        if (Loading)
        {
            output.Attributes.SetAttribute("data-loading", "true");
            output.Attributes.SetAttribute("aria-busy", "true");
        }
        if (!string.IsNullOrWhiteSpace(AriaLabel))
            output.Attributes.SetAttribute("aria-label", AriaLabel);

        var inner = (await output.GetChildContentAsync()).GetContent();

        var html = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(Icon))
            html.Append(IconHtml(Icon!, leading: !IconOnly));

        if (!IconOnly && !string.IsNullOrWhiteSpace(inner))
            html.Append("<span class=\"btn-label\">").Append(inner).Append("</span>");

        if (!string.IsNullOrWhiteSpace(IconTrailing))
            html.Append(IconHtml(IconTrailing!, leading: false, trailing: true));

        output.Content.SetHtmlContent(html.ToString());
    }

    private string IconHtml(string name, bool leading = true, bool trailing = false)
    {
        var svg = _icons.GetSvg(name);
        var cls = leading && !trailing ? "btn-icon-leading" : trailing ? "btn-icon-trailing" : "";
        if (svg is null)
            return $"<span class=\"es-icon es-icon--missing {cls}\" aria-hidden=\"true\"></span>";

        // inject class + aria-hidden into SVG root
        var idx = svg.IndexOf('>');
        if (idx < 0) return svg;
        var open = svg[..idx];
        var rest = svg[idx..];
        if (!open.Contains("class="))
            open += $" class=\"es-icon {cls}\"";
        else
            open = System.Text.RegularExpressions.Regex.Replace(open, "class=\"([^\"]*)\"", $"class=\"$1 es-icon {cls}\"".Replace("  ", " "));
        if (!open.Contains("aria-hidden"))
            open += " aria-hidden=\"true\" focusable=\"false\"";
        return open + rest;
    }

    private static string VariantClass(string variant) => variant?.ToLowerInvariant() switch
    {
        "primary" => "btn-primary",
        "navy" => "btn-navy",
        "secondary" => "btn-secondary",
        "ghost" => "btn-ghost",
        "danger" => "btn-danger",
        "success" => "btn-success",
        "dark" => "btn-dark",
        _ => "btn-primary"
    };

    private static string SizeClass(string size) => size?.ToLowerInvariant() switch
    {
        "sm" => "btn-sm",
        "lg" => "btn-lg",
        "md" => "",
        _ => ""
    };
}
