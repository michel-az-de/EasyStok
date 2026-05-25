using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

[HtmlTargetElement("es-kbd")]
public sealed class EsKbdTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "kbd";
        output.TagMode = TagMode.StartTagAndEndTag;

        var classes = new List<string> { "es-kbd" };
        var existing = output.Attributes["class"]?.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(existing)) classes.Add(existing);
        output.Attributes.SetAttribute("class", string.Join(" ", classes));

        var inner = (await output.GetChildContentAsync()).GetContent();
        output.Content.SetHtmlContent(inner);
    }
}
