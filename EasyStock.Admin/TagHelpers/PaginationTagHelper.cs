using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Paginação server-side (querystring source-of-truth).
/// Preserva todos os outros params da query atual (filtros, sort, etc.).
///
/// Uso:
///   <es-pagination page="@Model.Page" page-size="@Model.PageSize" total="@Model.Total" />
///   <es-pagination page="3" page-size="25" total="247" page-sizes="10,25,50,100" />
/// </summary>
[HtmlTargetElement("es-pagination")]
public sealed class PaginationTagHelper : TagHelper
{
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    public int Page { get; set; } = 1;

    [HtmlAttributeName("page-size")]
    public int PageSize { get; set; } = 25;

    public int Total { get; set; }

    [HtmlAttributeName("page-sizes")]
    public string PageSizes { get; set; } = "10,25,50,100";

    [HtmlAttributeName("page-param")]
    public string PageParam { get; set; } = "page";

    [HtmlAttributeName("size-param")]
    public string SizeParam { get; set; } = "pageSize";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize)));
        var safePage = Math.Clamp(Page, 1, totalPages);
        var firstItem = Total == 0 ? 0 : (safePage - 1) * PageSize + 1;
        var lastItem = Math.Min(safePage * PageSize, Total);

        output.TagName = "nav";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-pagination");
        output.Attributes.SetAttribute("aria-label", "Paginação");

        var sb = new StringBuilder();

        // Info
        sb.Append("<div class=\"es-pagination-info\">");
        if (Total == 0)
        {
            sb.Append("Nenhum registro");
        }
        else
        {
            sb.Append($"Mostrando <strong style=\"color:var(--text-primary);\">{firstItem:N0}–{lastItem:N0}</strong> de <strong style=\"color:var(--text-primary);\">{Total:N0}</strong>");
        }
        sb.Append("</div>");

        // Page-size selector
        sb.Append("<div class=\"es-pagination-size\">");
        sb.Append("<span>Por página</span>");
        sb.Append("<select aria-label=\"Itens por página\" onchange=\"window.location.href = this.value\">");
        foreach (var raw in PageSizes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(raw, out var sz) || sz <= 0) continue;
            var url = BuildUrl(1, sz);
            var selected = sz == PageSize ? " selected" : "";
            sb.Append($"<option value=\"{WebUtility.HtmlEncode(url)}\"{selected}>{sz}</option>");
        }
        sb.Append("</select>");
        sb.Append("</div>");

        // Controls
        sb.Append("<div class=\"es-pagination-controls\">");

        // Prev
        AppendNav(sb, safePage > 1, BuildUrl(safePage - 1, PageSize), "Página anterior", "chevron-left");

        // Page numbers
        foreach (var p in ComputeVisiblePages(safePage, totalPages))
        {
            if (p == -1)
            {
                sb.Append("<span class=\"es-pagination-ellipsis\" aria-hidden=\"true\">…</span>");
            }
            else if (p == safePage)
            {
                sb.Append($"<span class=\"es-pagination-page is-current\" aria-current=\"page\">{p}</span>");
            }
            else
            {
                sb.Append($"<a class=\"es-pagination-page\" href=\"{WebUtility.HtmlEncode(BuildUrl(p, PageSize))}\" aria-label=\"Página {p}\">{p}</a>");
            }
        }

        // Next
        AppendNav(sb, safePage < totalPages, BuildUrl(safePage + 1, PageSize), "Próxima página", "chevron-right");

        sb.Append("</div>");

        output.Content.SetHtmlContent(sb.ToString());
    }

    private static void AppendNav(StringBuilder sb, bool enabled, string url, string label, string iconName)
    {
        if (enabled)
        {
            sb.Append($"<a class=\"es-pagination-page\" href=\"{WebUtility.HtmlEncode(url)}\" aria-label=\"{WebUtility.HtmlEncode(label)}\">");
        }
        else
        {
            sb.Append("<span class=\"es-pagination-page is-disabled\" aria-disabled=\"true\">");
        }
        sb.Append($"<svg class=\"es-icon\" width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-{iconName}\"/></svg>");
        sb.Append(enabled ? "</a>" : "</span>");
    }

    private string BuildUrl(int page, int pageSize)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in ViewContext.HttpContext.Request.Query)
        {
            if (kv.Key.Equals(PageParam, StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals(SizeParam, StringComparison.OrdinalIgnoreCase)) continue;
            query[kv.Key] = kv.Value.ToString();
        }
        if (page > 1) query[PageParam] = page.ToString();
        query[SizeParam] = pageSize.ToString();
        var path = ViewContext.HttpContext.Request.Path.ToString();
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return string.IsNullOrEmpty(qs) ? path : $"{path}?{qs}";
    }

    /// <summary>Retorna [1, -1 (ellipsis), …, current-1, current, current+1, …, -1, total]</summary>
    private static IEnumerable<int> ComputeVisiblePages(int current, int total)
    {
        if (total <= 7)
        {
            for (var i = 1; i <= total; i++) yield return i;
            yield break;
        }

        yield return 1;
        if (current > 4) yield return -1;
        for (var i = Math.Max(2, current - 1); i <= Math.Min(total - 1, current + 1); i++) yield return i;
        if (current < total - 3) yield return -1;
        yield return total;
    }
}
