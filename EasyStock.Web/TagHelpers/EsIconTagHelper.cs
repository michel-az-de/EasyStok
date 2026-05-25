using System.Text.RegularExpressions;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

[HtmlTargetElement("es-icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class EsIconTagHelper : TagHelper
{
    private static readonly Regex SvgOpenTag = new(@"<svg\b[^>]*>", RegexOptions.Compiled);
    private static readonly Regex ClassAttr = new("class=\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex StrokeWidthAttr = new("stroke-width=\"([^\"]*)\"", RegexOptions.Compiled);

    private readonly LucideIconResolver _resolver;

    public EsIconTagHelper(LucideIconResolver resolver)
    {
        _resolver = resolver;
    }

    [HtmlAttributeName("name")]
    public string? Name { get; set; }

    [HtmlAttributeName("class")]
    public string? Class { get; set; }

    [HtmlAttributeName("stroke-width")]
    public string? StrokeWidth { get; set; }

    [HtmlAttributeName("aria-label")]
    public string? AriaLabel { get; set; }

    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            output.SuppressOutput();
            return;
        }

        var raw = _resolver.GetSvg(Name);
        if (raw is null)
        {
            output.Content.SetHtmlContent(BuildFallback(Class));
            return;
        }

        var rendered = SvgOpenTag.Replace(raw, match =>
        {
            var tag = match.Value;
            tag = MergeClass(tag, Class);
            if (!string.IsNullOrWhiteSpace(StrokeWidth))
                tag = ReplaceOrAppend(tag, StrokeWidthAttr, "stroke-width", StrokeWidth);

            if (!string.IsNullOrWhiteSpace(AriaLabel))
            {
                tag = AppendAttr(tag, "role", "img");
                tag = AppendAttr(tag, "aria-label", AriaLabel);
            }
            else
            {
                tag = AppendAttr(tag, "aria-hidden", "true");
                tag = AppendAttr(tag, "focusable", "false");
            }

            if (!string.IsNullOrWhiteSpace(Title))
                tag = AppendAttr(tag, "data-tip", Title);

            return tag;
        }, 1);

        output.Content.SetHtmlContent(rendered);
    }

    private static string MergeClass(string svgOpenTag, string? extraClass)
    {
        var classes = "es-icon";
        if (!string.IsNullOrWhiteSpace(extraClass))
            classes += " " + extraClass.Trim();

        var match = ClassAttr.Match(svgOpenTag);
        if (match.Success)
        {
            var existing = match.Groups[1].Value;
            var combined = string.IsNullOrWhiteSpace(existing) ? classes : existing + " " + classes;
            return ClassAttr.Replace(svgOpenTag, $"class=\"{combined}\"", 1);
        }
        return AppendAttr(svgOpenTag, "class", classes);
    }

    private static string ReplaceOrAppend(string tag, Regex attrRegex, string attrName, string value)
    {
        if (attrRegex.IsMatch(tag))
            return attrRegex.Replace(tag, $"{attrName}=\"{HtmlEncode(value)}\"", 1);
        return AppendAttr(tag, attrName, value);
    }

    private static string AppendAttr(string tag, string attr, string value)
    {
        var insertAt = tag.LastIndexOf('>');
        if (insertAt < 0) return tag;
        var selfClose = tag[insertAt - 1] == '/';
        var prefix = tag[..(selfClose ? insertAt - 1 : insertAt)].TrimEnd();
        var suffix = selfClose ? " />" : ">";
        return $"{prefix} {attr}=\"{HtmlEncode(value)}\"{suffix}";
    }

    private static string HtmlEncode(string s) =>
        System.Net.WebUtility.HtmlEncode(s);

    private static string BuildFallback(string? extraClass)
    {
        var classes = "es-icon es-icon--missing";
        if (!string.IsNullOrWhiteSpace(extraClass))
            classes += " " + extraClass.Trim();
        return $"<span class=\"{HtmlEncode(classes)}\" aria-hidden=\"true\"></span>";
    }
}
