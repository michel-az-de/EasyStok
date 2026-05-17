using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Skeleton loader (shimmer). Usado enquanto dados ainda não chegaram do servidor.
///
/// Uso:
///   <es-skeleton variant="title" width="180" />
///   <es-skeleton variant="num" />
///   <es-skeleton variant="block" height="120" />
/// </summary>
[HtmlTargetElement("es-skeleton", TagStructure = TagStructure.WithoutEndTag)]
public sealed class SkeletonTagHelper : TagHelper
{
    /// <summary>text | num | title | block | custom</summary>
    public string Variant { get; set; } = "text";

    /// <summary>CSS width — accepts "120", "120px", "60%". Number-only is treated as px.</summary>
    public string? Width { get; set; }

    /// <summary>CSS height — accepts "120", "120px". Number-only is treated as px.</summary>
    public string? Height { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"skeleton skeleton-{Variant}");
        output.Attributes.SetAttribute("aria-hidden", "true");

        var styles = new List<string>();
        if (!string.IsNullOrWhiteSpace(Width)) styles.Add($"width: {NormalizeUnit(Width!)}");
        if (!string.IsNullOrWhiteSpace(Height)) styles.Add($"height: {NormalizeUnit(Height!)}");
        if (styles.Count > 0)
        {
            output.Attributes.SetAttribute("style", string.Join("; ", styles));
        }

        output.Content.SetHtmlContent(string.Empty);
    }

    private static string NormalizeUnit(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.EndsWith('%') || trimmed.EndsWith("px") || trimmed.EndsWith("em") || trimmed.EndsWith("rem")) return trimmed;
        return double.TryParse(trimmed, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)
            ? trimmed + "px" : trimmed;
    }
}
