using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

internal sealed class CardSlots
{
    public string? FooterHtml { get; set; }
    public string? HeaderActionsHtml { get; set; }
}

/// <summary>
/// Card com slots opcionais via tag helpers filhos:
///   <es-card-footer>...</es-card-footer>
///   <es-card-header-actions>...</es-card-header-actions>
///
/// Uso:
///   <es-card title="Resumo" description="Últimos 30 dias">
///       <p>Conteúdo do card...</p>
///       <es-card-footer>
///           <es-button variant="ghost" size="sm">Cancelar</es-button>
///           <es-button variant="primary" size="sm">Salvar</es-button>
///       </es-card-footer>
///   </es-card>
/// </summary>
[HtmlTargetElement("es-card")]
public sealed class CardTagHelper : TagHelper
{
    private const string SlotsKey = "es-card:slots";

    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>none | sm | md (default) | lg</summary>
    public string Padding { get; set; } = "md";

    [HtmlAttributeName("card-class")]
    public string? CardClass { get; set; }

    public override void Init(TagHelperContext context)
    {
        context.Items[SlotsKey] = new CardSlots();
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "section";
        output.TagMode = TagMode.StartTagAndEndTag;

        var classes = "es-card" + (string.IsNullOrEmpty(CardClass) ? "" : " " + CardClass);
        output.Attributes.SetAttribute("class", classes);

        // Children — body do card. Slots filhos suppressam seu próprio output e populam slots.
        var content = await output.GetChildContentAsync();
        var body = content.GetContent() ?? string.Empty;

        var slots = (context.Items.TryGetValue(SlotsKey, out var v) && v is CardSlots s) ? s : new CardSlots();

        var sb = new StringBuilder();
        var hasHeader = !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Description) || !string.IsNullOrWhiteSpace(slots.HeaderActionsHtml);
        if (hasHeader)
        {
            sb.Append("<header class=\"es-card-header\">");
            sb.Append("<div>");
            if (!string.IsNullOrWhiteSpace(Title))
                sb.Append($"<h3 class=\"es-card-title\">{WebUtility.HtmlEncode(Title)}</h3>");
            if (!string.IsNullOrWhiteSpace(Description))
                sb.Append($"<div class=\"es-card-desc\">{WebUtility.HtmlEncode(Description)}</div>");
            sb.Append("</div>");
            if (!string.IsNullOrWhiteSpace(slots.HeaderActionsHtml))
            {
                sb.Append("<div style=\"display:flex;gap:8px;\">");
                sb.Append(slots.HeaderActionsHtml);
                sb.Append("</div>");
            }
            sb.Append("</header>");
        }

        var bodyClass = Padding == "none" ? "es-card-body es-card-body-flush" : "es-card-body";
        sb.Append($"<div class=\"{bodyClass}\">");
        sb.Append(body);
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(slots.FooterHtml))
        {
            sb.Append("<footer class=\"es-card-footer\">");
            sb.Append(slots.FooterHtml);
            sb.Append("</footer>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }
}

[HtmlTargetElement("es-card-footer", ParentTag = "es-card")]
public sealed class CardFooterTagHelper : TagHelper
{
    private const string SlotsKey = "es-card:slots";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is CardSlots slots)
        {
            slots.FooterHtml = content.GetContent();
        }
        output.SuppressOutput();
    }
}

[HtmlTargetElement("es-card-header-actions", ParentTag = "es-card")]
public sealed class CardHeaderActionsTagHelper : TagHelper
{
    private const string SlotsKey = "es-card:slots";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is CardSlots slots)
        {
            slots.HeaderActionsHtml = content.GetContent();
        }
        output.SuppressOutput();
    }
}
