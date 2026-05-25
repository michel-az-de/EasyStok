using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

internal sealed class TabsSlots
{
    public List<TabInfo> Tabs { get; } = new();
}

internal sealed class TabInfo
{
    public string Key { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Count { get; set; }
    public string? Icon { get; set; }
    public string? ContentHtml { get; set; }
}

/// <summary>
/// Tabs com estado persistido em ?tab= e ARIA completo.
///
/// Uso:
///   <es-tabs default-tab="overview">
///       <es-tab key="overview" label="Visão geral" icon="layout-dashboard">
///           <p>Conteúdo da visão geral...</p>
///       </es-tab>
///       <es-tab key="faturas" label="Faturas" count="12">
///           <es-data-table>...</es-data-table>
///       </es-tab>
///       <es-tab key="audit" label="Audit" icon="search">
///           <p>Histórico do tenant...</p>
///       </es-tab>
///   </es-tabs>
/// </summary>
[HtmlTargetElement("es-tabs")]
public sealed class TabsTagHelper : TagHelper
{
    private const string SlotsKey = "es-tabs:slots";

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    [HtmlAttributeName("default-tab")]
    public string? DefaultTab { get; set; }

    public override void Init(TagHelperContext context)
    {
        context.Items[SlotsKey] = new TabsSlots();
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Processa children primeiro pra popular as tabs.
        await output.GetChildContentAsync();

        var slots = (context.Items.TryGetValue(SlotsKey, out var v) && v is TabsSlots s) ? s : new TabsSlots();
        if (slots.Tabs.Count == 0)
        {
            output.SuppressOutput();
            return;
        }

        // Resolve active tab: ?tab= → DefaultTab → primeira
        var activeKey = ViewContext.HttpContext.Request.Query.TryGetValue("tab", out var t) ? t.ToString() : null;
        if (string.IsNullOrWhiteSpace(activeKey) || !slots.Tabs.Any(x => x.Key == activeKey))
        {
            activeKey = !string.IsNullOrWhiteSpace(DefaultTab) && slots.Tabs.Any(x => x.Key == DefaultTab)
                ? DefaultTab
                : slots.Tabs[0].Key;
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-tabs");
        output.Attributes.SetAttribute("x-data", $"esTabs({HtmlSafeJsString(activeKey)})");

        var sb = new StringBuilder();

        // Tablist
        sb.Append("<div class=\"es-tablist\" role=\"tablist\">");
        foreach (var tab in slots.Tabs)
        {
            var safeKey = WebUtility.HtmlEncode(tab.Key);
            var keyJs = HtmlSafeJsString(tab.Key);
            sb.Append("<button type=\"button\" class=\"es-tab\"");
            sb.Append($" :class=\"isActive({keyJs}) ? 'is-active' : ''\"");
            sb.Append($" @click=\"select({keyJs})\"");
            sb.Append($" role=\"tab\" id=\"tab-{safeKey}\" aria-controls=\"panel-{safeKey}\"");
            sb.Append($" :aria-selected=\"isActive({keyJs}) ? 'true' : 'false'\"");
            sb.Append($" :tabindex=\"isActive({keyJs}) ? '0' : '-1'\"");
            sb.Append('>');

            if (!string.IsNullOrWhiteSpace(tab.Icon))
            {
                var safeIcon = WebUtility.HtmlEncode(tab.Icon);
                sb.Append($"<svg class=\"es-icon\" width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-{safeIcon}\"/></svg>");
            }
            sb.Append(WebUtility.HtmlEncode(tab.Label ?? tab.Key));
            if (!string.IsNullOrWhiteSpace(tab.Count))
            {
                sb.Append($"<span class=\"es-tab-count\">{WebUtility.HtmlEncode(tab.Count)}</span>");
            }
            sb.Append("</button>");
        }
        sb.Append("</div>");

        // Panels
        foreach (var tab in slots.Tabs)
        {
            var safeKey = WebUtility.HtmlEncode(tab.Key);
            var keyJs = HtmlSafeJsString(tab.Key);
            sb.Append("<div class=\"es-tabpanel\" role=\"tabpanel\"");
            sb.Append($" id=\"panel-{safeKey}\" aria-labelledby=\"tab-{safeKey}\"");
            sb.Append($" tabindex=\"0\" x-show=\"isActive({keyJs})\" x-cloak");
            sb.Append('>');
            sb.Append(tab.ContentHtml ?? string.Empty);
            sb.Append("</div>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }

    private static string HtmlSafeJsString(string? input)
    {
        var s = input ?? string.Empty;
        // Aspas simples + escape de \\ e ' para uso em atributo HTML que vira string JS.
        s = s.Replace("\\", "\\\\").Replace("'", "\\'");
        return $"'{s}'";
    }
}

[HtmlTargetElement("es-tab", ParentTag = "es-tabs")]
public sealed class TabTagHelper : TagHelper
{
    private const string SlotsKey = "es-tabs:slots";

    public string Key { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Count { get; set; }
    public string? Icon { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is TabsSlots slots && !string.IsNullOrWhiteSpace(Key))
        {
            slots.Tabs.Add(new TabInfo
            {
                Key = Key,
                Label = Label,
                Count = Count,
                Icon = Icon,
                ContentHtml = content.GetContent()
            });
        }
        output.SuppressOutput();
    }
}
