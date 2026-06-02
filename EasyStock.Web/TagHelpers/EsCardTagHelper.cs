using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

internal sealed class EsCardSlots
{
    public string? FooterHtml { get; set; }
    public string? HeaderActionsHtml { get; set; }
}

/// <summary>
/// Card com slots opcionais (es-card-footer, es-card-header-actions).
/// Port do EasyStock.Admin/TagHelpers/CardTagHelper.cs (convergencia DS).
///
/// Uso:
///   &lt;es-card title="Resumo" description="Ultimos 30 dias"&gt;
///       &lt;p&gt;Conteudo...&lt;/p&gt;
///       &lt;es-card-footer&gt;&lt;es-button variant="primary"&gt;Salvar&lt;/es-button&gt;&lt;/es-card-footer&gt;
///   &lt;/es-card&gt;
/// </summary>
[HtmlTargetElement("es-card")]
public sealed class EsCardTagHelper : TagHelper
{
    private const string SlotsKey = "es-card:slots";

    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>none | sm | md (default) | lg</summary>
    public string Padding { get; set; } = "md";

    [HtmlAttributeName("card-class")]
    public string? CardClass { get; set; }

    public override void Init(TagHelperContext context) => context.Items[SlotsKey] = new EsCardSlots();

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "section";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-card" + (string.IsNullOrEmpty(CardClass) ? "" : " " + CardClass));

        var content = await output.GetChildContentAsync();
        var body = content.GetContent() ?? string.Empty;
        var slots = (context.Items.TryGetValue(SlotsKey, out var v) && v is EsCardSlots s) ? s : new EsCardSlots();

        var sb = new StringBuilder();
        var hasHeader = !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Description) || !string.IsNullOrWhiteSpace(slots.HeaderActionsHtml);
        if (hasHeader)
        {
            sb.Append("<header class=\"es-card-header\"><div>");
            if (!string.IsNullOrWhiteSpace(Title))
                sb.Append($"<h3 class=\"es-card-title\">{WebUtility.HtmlEncode(Title)}</h3>");
            if (!string.IsNullOrWhiteSpace(Description))
                sb.Append($"<div class=\"es-card-desc\">{WebUtility.HtmlEncode(Description)}</div>");
            sb.Append("</div>");
            if (!string.IsNullOrWhiteSpace(slots.HeaderActionsHtml))
                sb.Append("<div style=\"display:flex;gap:8px;\">").Append(slots.HeaderActionsHtml).Append("</div>");
            sb.Append("</header>");
        }

        var bodyClass = Padding == "none" ? "es-card-body es-card-body-flush" : "es-card-body";
        sb.Append($"<div class=\"{bodyClass}\">").Append(body).Append("</div>");

        if (!string.IsNullOrWhiteSpace(slots.FooterHtml))
            sb.Append("<footer class=\"es-card-footer\">").Append(slots.FooterHtml).Append("</footer>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

[HtmlTargetElement("es-card-footer", ParentTag = "es-card")]
public sealed class EsCardFooterTagHelper : TagHelper
{
    private const string SlotsKey = "es-card:slots";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is EsCardSlots slots)
            slots.FooterHtml = content.GetContent();
        output.SuppressOutput();
    }
}

[HtmlTargetElement("es-card-header-actions", ParentTag = "es-card")]
public sealed class EsCardHeaderActionsTagHelper : TagHelper
{
    private const string SlotsKey = "es-card:slots";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is EsCardSlots slots)
            slots.HeaderActionsHtml = content.GetContent();
        output.SuppressOutput();
    }
}
