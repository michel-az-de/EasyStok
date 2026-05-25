using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Breadcrumb navigation. Cada item é um <es-breadcrumb-item>; o último (sem href)
/// é marcado automaticamente com aria-current="page".
///
/// Uso:
///   <es-breadcrumb>
///       <es-breadcrumb-item href="/">Início</es-breadcrumb-item>
///       <es-breadcrumb-item href="/Tenants">Clientes</es-breadcrumb-item>
///       <es-breadcrumb-item>Acme Corp</es-breadcrumb-item>
///   </es-breadcrumb>
/// </summary>
[HtmlTargetElement("es-breadcrumb")]
public sealed class BreadcrumbTagHelper : TagHelper
{
    [HtmlAttributeName("aria-label")]
    public string AriaLabel { get; set; } = "Você está em";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "nav";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("aria-label", AriaLabel);
        output.Attributes.SetAttribute("class", "es-breadcrumb");

        var content = await output.GetChildContentAsync();
        var inner = content.GetContent() ?? string.Empty;

        var sb = new StringBuilder();
        sb.Append("<ol style=\"display:flex;flex-wrap:wrap;gap:6px;list-style:none;padding:0;margin:0;align-items:center;\">");
        sb.Append(inner);
        sb.Append("</ol>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

[HtmlTargetElement("es-breadcrumb-item", ParentTag = "es-breadcrumb")]
public sealed class BreadcrumbItemTagHelper : TagHelper
{
    public string? Href { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "li";
        output.TagMode = TagMode.StartTagAndEndTag;

        var isCurrent = string.IsNullOrWhiteSpace(Href);
        output.Attributes.SetAttribute("class", "es-breadcrumb-item" + (isCurrent ? " is-current" : ""));
        if (isCurrent) output.Attributes.SetAttribute("aria-current", "page");

        var content = await output.GetChildContentAsync();
        var label = content.GetContent() ?? string.Empty;

        var sb = new StringBuilder();
        if (!isCurrent)
        {
            sb.Append($"<a href=\"{WebUtility.HtmlEncode(Href!)}\">{label}</a>");
        }
        else
        {
            sb.Append("<span>").Append(label).Append("</span>");
        }
        output.Content.SetHtmlContent(sb.ToString());
    }
}
