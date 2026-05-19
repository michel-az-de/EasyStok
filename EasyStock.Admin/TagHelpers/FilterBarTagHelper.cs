using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

internal sealed class FilterBarContext
{
    public bool HasChips { get; set; }
}

/// <summary>
/// Filter bar — container de chips removíveis lendo querystring.
/// O botão "Limpar filtros" remove todos os params exceto os ignorados (page, pageSize, orderBy, dir).
///
/// Uso:
///   <es-filter-bar>
///       @if (!string.IsNullOrEmpty(Model.Status)) {
///           <es-filter-chip label="Status" remove-param="status">@Model.Status</es-filter-chip>
///       }
///       @if (!string.IsNullOrEmpty(Model.Q)) {
///           <es-filter-chip label="Busca" remove-param="q">@Model.Q</es-filter-chip>
///       }
///   </es-filter-bar>
/// </summary>
[HtmlTargetElement("es-filter-bar")]
public sealed class FilterBarTagHelper : TagHelper
{
    private const string CtxKey = "es-filter-bar:ctx";

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>CSV de params a preservar ao "Limpar". Default: page, pageSize, size, orderBy, dir, tab</summary>
    [HtmlAttributeName("preserve")]
    public string Preserve { get; set; } = "page,pageSize,size,orderBy,dir,tab";

    [HtmlAttributeName("show-clear")]
    public bool ShowClear { get; set; } = true;

    public override void Init(TagHelperContext context)
    {
        context.Items[CtxKey] = new FilterBarContext();
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-filter-bar");
        output.Attributes.SetAttribute("aria-label", "Filtros aplicados");
        output.Attributes.SetAttribute("role", "toolbar");

        var content = await output.GetChildContentAsync();
        var chipsHtml = content.GetContent() ?? string.Empty;
        var ctx = (context.Items.TryGetValue(CtxKey, out var v) && v is FilterBarContext c) ? c : new FilterBarContext();

        var sb = new StringBuilder();
        sb.Append(chipsHtml);

        if (ShowClear && ctx.HasChips)
        {
            var url = BuildClearUrl();
            sb.Append($"<a class=\"es-filter-clear\" href=\"{WebUtility.HtmlEncode(url)}\">Limpar filtros</a>");
        }

        // Quando não há nenhum chip, escondemos o bar inteiro (sem altura, sem padding).
        if (!ctx.HasChips)
        {
            output.Attributes.SetAttribute("style", "display:none;");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }

    private string BuildClearUrl()
    {
        var preserve = new HashSet<string>(
            Preserve.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in ViewContext.HttpContext.Request.Query)
        {
            if (preserve.Contains(kv.Key))
                query[kv.Key] = kv.Value.ToString();
        }
        var path = ViewContext.HttpContext.Request.Path.ToString();
        if (query.Count == 0) return path;
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{path}?{qs}";
    }
}

/// <summary>
/// Chip de filtro removível. O X navega para a URL atual sem o param removido.
/// </summary>
[HtmlTargetElement("es-filter-chip", ParentTag = "es-filter-bar")]
public sealed class FilterChipTagHelper : TagHelper
{
    private const string CtxKey = "es-filter-bar:ctx";

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    public string? Label { get; set; }

    [HtmlAttributeName("remove-param")]
    public string? RemoveParam { get; set; }

    [HtmlAttributeName("remove-params")]
    public string? RemoveParams { get; set; }

    public string? Value { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        var inner = string.IsNullOrEmpty(Value) ? content.GetContent() : Value;
        if (string.IsNullOrWhiteSpace(inner)) { output.SuppressOutput(); return; }

        if (context.Items.TryGetValue(CtxKey, out var v) && v is FilterBarContext ctx)
        {
            ctx.HasChips = true;
        }

        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-filter-chip");

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Label))
        {
            sb.Append("<span class=\"es-filter-chip-label\">");
            sb.Append(WebUtility.HtmlEncode(Label));
            sb.Append("</span>");
        }
        sb.Append("<span>").Append(inner).Append("</span>");

        var paramsToRemove = ResolveRemoveParams();
        if (paramsToRemove.Count > 0)
        {
            var url = BuildRemoveUrl(paramsToRemove);
            sb.Append($"<a class=\"es-filter-chip-remove\" href=\"{WebUtility.HtmlEncode(url)}\" aria-label=\"Remover filtro\">");
            sb.Append("<svg class=\"es-icon\" width=\"12\" height=\"12\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-x\"/></svg>");
            sb.Append("</a>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }

    private List<string> ResolveRemoveParams()
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(RemoveParam)) list.Add(RemoveParam!);
        if (!string.IsNullOrWhiteSpace(RemoveParams))
        {
            list.AddRange(RemoveParams!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return list;
    }

    private string BuildRemoveUrl(List<string> remove)
    {
        var skip = new HashSet<string>(remove, StringComparer.OrdinalIgnoreCase);
        // Sempre volta pra página 1 ao mudar filtro.
        skip.Add("page");
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in ViewContext.HttpContext.Request.Query)
        {
            if (skip.Contains(kv.Key)) continue;
            query[kv.Key] = kv.Value.ToString();
        }
        var path = ViewContext.HttpContext.Request.Path.ToString();
        if (query.Count == 0) return path;
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{path}?{qs}";
    }
}
