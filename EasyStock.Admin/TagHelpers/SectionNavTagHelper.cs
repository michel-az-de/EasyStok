using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

internal sealed class SectionNavSlots
{
    public StringBuilder ItemsHtml { get; } = new();
}

/// <summary>
/// Sub-navegação ENTRE PÁGINAS (pills com link). Cada item é um
/// &lt;es-section-nav-item&gt; com href; o item da página atual recebe
/// active="true" e é marcado com aria-current="page".
///
/// Convenção do Admin: sub-seções ENTRE páginas usam &lt;es-section-nav&gt;;
/// abas de conteúdo na MESMA página usam &lt;es-tabs&gt;. Substitui o padrão manual
/// de _Subnav.cshtml (nav + &lt;a&gt; com classe active). A página decide qual item
/// está ativo (ex.: por prefixo de rota no Razor).
///
/// Uso:
///   &lt;es-section-nav aria-label="Sub-navegação de Notificações"&gt;
///       &lt;es-section-nav-item href="/Notificacoes/Templates" icon="file-text"
///                             active="true"&gt;Templates&lt;/es-section-nav-item&gt;
///       &lt;es-section-nav-item href="/Notificacoes/Rotinas" icon="settings"&gt;Rotinas&lt;/es-section-nav-item&gt;
///       &lt;es-section-nav-item href="/Notificacoes/Canais" icon="radio"&gt;Canais&lt;/es-section-nav-item&gt;
///   &lt;/es-section-nav&gt;
/// </summary>
[HtmlTargetElement("es-section-nav")]
public sealed class SectionNavTagHelper : TagHelper
{
    internal const string SlotsKey = "es-section-nav:slots";

    [HtmlAttributeName("aria-label")]
    public string AriaLabel { get; set; } = "Sub-navegação";

    public override void Init(TagHelperContext context)
    {
        context.Items[SlotsKey] = new SectionNavSlots();
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Processa os filhos primeiro para popular os pills.
        await output.GetChildContentAsync();

        var slots = (context.Items.TryGetValue(SlotsKey, out var v) && v is SectionNavSlots s)
            ? s
            : new SectionNavSlots();

        output.TagName = "nav";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-section-nav");
        output.Attributes.SetAttribute("aria-label", AriaLabel);

        var sb = new StringBuilder();
        sb.Append("<ul class=\"es-section-nav-list\">");
        sb.Append(slots.ItemsHtml.ToString());
        sb.Append("</ul>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>
/// Item (pill) de &lt;es-section-nav&gt;. Renderiza um &lt;a href&gt; dentro de um &lt;li&gt;.
/// Quando active="true", recebe a classe is-active e aria-current="page".
/// O conteúdo-filho é o rótulo; icon="..." prefixa um SVG do sprite.
/// </summary>
[HtmlTargetElement("es-section-nav-item", ParentTag = "es-section-nav")]
public sealed class SectionNavItemTagHelper : TagHelper
{
    public string Href { get; set; } = "#";
    public bool Active { get; set; }
    public string? Icon { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        var label = content.GetContent() ?? string.Empty;

        var sb = new StringBuilder();
        sb.Append("<li class=\"es-section-nav-item\">");

        var linkClass = Active ? "es-section-nav-link is-active" : "es-section-nav-link";
        sb.Append($"<a class=\"{linkClass}\" href=\"{WebUtility.HtmlEncode(Href)}\"");
        if (Active) sb.Append(" aria-current=\"page\"");
        sb.Append('>');

        if (!string.IsNullOrWhiteSpace(Icon))
        {
            var safeIcon = WebUtility.HtmlEncode(Icon);
            sb.Append("<svg class=\"es-icon\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" " +
                      "fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" " +
                      "stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">" +
                      $"<use href=\"/icons/sprite.svg#icon-{safeIcon}\"/></svg>");
        }

        sb.Append($"<span class=\"es-section-nav-text\">{WebUtility.HtmlEncode(label)}</span>");
        sb.Append("</a>");
        sb.Append("</li>");

        if (context.Items.TryGetValue(SectionNavTagHelper.SlotsKey, out var v) && v is SectionNavSlots slots)
        {
            slots.ItemsHtml.Append(sb.ToString());
        }

        output.SuppressOutput();
    }
}
