using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

internal sealed class DataTableSlots
{
    public List<DataTableColumnInfo> Columns { get; } = new();
    public string? EmptyHtml { get; set; }
    public string? BulkActionsHtml { get; set; }
}

internal sealed class DataTableColumnInfo
{
    public string? Key { get; set; }
    public string? Label { get; set; }
    public bool Sortable { get; set; }
    public string Align { get; set; } = "left";
    public bool Numeric { get; set; }
    public bool Actions { get; set; }
    public string? Width { get; set; }
}

/// <summary>
/// Tabela completa com sort por header (querystring), bulk select via Alpine,
/// empty state e bulk actions slots, e mobile reflow para cards (table-stack).
///
/// Uso:
///   <es-data-table sort-key="@Model.OrderBy" sort-dir="@Model.Direction"
///                  bulk-name="ids" is-empty="@(!Model.Items.Any())">
///       <es-data-table-column key="codigo" label="Código" sortable="true" />
///       <es-data-table-column key="valor"  label="Valor"  align="right" sortable="true" />
///       <es-data-table-column label="" actions="true" />
///
///       @foreach (var i in Model.Items) {
///           <tr>
///               <td><input type="checkbox" name="ids" value="@i.Id" data-bulk-row /></td>
///               <td data-label="Código"><strong>@i.Codigo</strong></td>
///               <td data-label="Valor" class="es-data-table-td-numeric">@i.Valor</td>
///               <td class="es-data-table-td-actions">
///                   <es-button variant="ghost" size="xs" icon="edit-2" icon-only="true" aria-label="Editar" />
///               </td>
///           </tr>
///       }
///
///       <es-data-table-empty>
///           <es-empty-state icon="tag" title="Nenhum cupom" description="Crie cupons.">
///               <es-button icon="plus">Novo</es-button>
///           </es-empty-state>
///       </es-data-table-empty>
///
///       <es-data-table-bulk-actions>
///           <es-button variant="danger" size="sm" icon="trash-2">Excluir selecionados</es-button>
///       </es-data-table-bulk-actions>
///   </es-data-table>
/// </summary>
[HtmlTargetElement("es-data-table")]
public sealed class DataTableTagHelper : TagHelper
{
    private const string SlotsKey = "es-data-table:slots";

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    [HtmlAttributeName("sort-key")]
    public string? SortKey { get; set; }

    [HtmlAttributeName("sort-dir")]
    public string? SortDir { get; set; }

    /// <summary>Quando preenchido, ativa bulk select e o name dos checkboxes individuais deve casar.</summary>
    [HtmlAttributeName("bulk-name")]
    public string? BulkName { get; set; }

    [HtmlAttributeName("is-empty")]
    public bool IsEmpty { get; set; }

    /// <summary>Quando true (default), tabela vira cards em &lt;768px via .table-stack.</summary>
    [HtmlAttributeName("table-stack")]
    public bool TableStack { get; set; } = true;

    public override void Init(TagHelperContext context)
    {
        context.Items[SlotsKey] = new DataTableSlots();
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-data-table-wrap");

        var hasBulk = !string.IsNullOrWhiteSpace(BulkName);
        if (hasBulk)
        {
            output.Attributes.SetAttribute("x-data", "esDataTable()");
        }

        var content = await output.GetChildContentAsync();
        var rowsHtml = content.GetContent() ?? string.Empty;
        var slots = (context.Items.TryGetValue(SlotsKey, out var v) && v is DataTableSlots s) ? s : new DataTableSlots();

        var sb = new StringBuilder();

        // Bulk action bar
        if (hasBulk)
        {
            sb.Append("<div class=\"es-data-table-bulk-bar\" x-show=\"selected.size > 0\" x-cloak>");
            sb.Append("<svg class=\"es-icon\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-check-circle\"/></svg>");
            sb.Append("<span class=\"es-data-table-bulk-bar-count\" x-text=\"selected.size + ' selecionado' + (selected.size > 1 ? 's' : '')\"></span>");
            sb.Append("<button type=\"button\" class=\"es-filter-clear\" @click=\"clearAll()\">LIMPAR</button>");
            sb.Append("<div class=\"es-data-table-bulk-bar-actions\">");
            if (!string.IsNullOrWhiteSpace(slots.BulkActionsHtml)) sb.Append(slots.BulkActionsHtml);
            sb.Append("</div>");
            sb.Append("</div>");
        }

        sb.Append("<div class=\"es-data-table-scroll\">");
        var tableClass = "es-data-table" + (TableStack ? " table-stack" : "");
        sb.Append($"<table class=\"{tableClass}\">");

        // ── thead ──
        sb.Append("<thead><tr>");
        if (hasBulk)
        {
            sb.Append("<th class=\"es-data-table-th-bulk\">");
            sb.Append("<label class=\"es-checkbox\" style=\"margin:0;\"><input type=\"checkbox\" :checked=\"allSelected\" :indeterminate=\"someSelected\" @change=\"toggleAll($event.target.checked)\" aria-label=\"Selecionar todas as linhas visíveis\"><span></span></label>");
            sb.Append("</th>");
        }
        foreach (var col in slots.Columns)
        {
            var classes = new List<string>();
            if (col.Numeric || col.Align == "right") classes.Add("es-data-table-td-numeric");
            if (col.Actions) classes.Add("es-data-table-td-actions");
            if (col.Sortable && !string.IsNullOrWhiteSpace(col.Key)) classes.Add("es-data-table-th-sortable");

            var isCurrent = !string.IsNullOrEmpty(SortKey) && SortKey == col.Key;
            var dir = NormalizeDir(SortDir);
            if (isCurrent) classes.Add("is-sorted");
            if (isCurrent && dir == "desc") classes.Add("is-sorted-desc");

            sb.Append("<th");
            if (classes.Count > 0) sb.Append(" class=\"").Append(string.Join(' ', classes)).Append('"');
            if (!string.IsNullOrWhiteSpace(col.Width)) sb.Append($" style=\"width:{WebUtility.HtmlEncode(col.Width)}\"");
            sb.Append(" scope=\"col\"");
            if (isCurrent) sb.Append($" aria-sort=\"{(dir == "desc" ? "descending" : "ascending")}\"");
            sb.Append('>');

            if (col.Sortable && !string.IsNullOrEmpty(col.Key))
            {
                var nextDir = (isCurrent && dir == "asc") ? "desc" : "asc";
                var url = BuildSortUrl(col.Key, nextDir);
                sb.Append($"<a href=\"{WebUtility.HtmlEncode(url)}\">");
                sb.Append(WebUtility.HtmlEncode(col.Label ?? col.Key));
                sb.Append(BuildSortIcon(isCurrent, dir));
                sb.Append("</a>");
            }
            else
            {
                sb.Append(WebUtility.HtmlEncode(col.Label ?? string.Empty));
            }
            sb.Append("</th>");
        }
        sb.Append("</tr></thead>");

        // ── tbody ──
        if (IsEmpty)
        {
            var totalCols = slots.Columns.Count + (hasBulk ? 1 : 0);
            sb.Append("<tbody><tr><td class=\"es-data-table-empty\" colspan=\"")
              .Append(Math.Max(1, totalCols)).Append("\">");
            sb.Append(slots.EmptyHtml ?? "<div style=\"padding:24px;\"><em>Nenhum registro encontrado.</em></div>");
            sb.Append("</td></tr></tbody>");
        }
        else
        {
            sb.Append("<tbody x-ref=\"tbody\"");
            if (hasBulk) sb.Append(" @change=\"onItemChange($event)\"");
            sb.Append('>');
            sb.Append(rowsHtml);
            sb.Append("</tbody>");
        }

        sb.Append("</table></div>");
        output.Content.SetHtmlContent(sb.ToString());
    }

    private string BuildSortUrl(string key, string dir)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in ViewContext.HttpContext.Request.Query)
        {
            if (kv.Key.Equals("orderBy", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("dir", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("page", StringComparison.OrdinalIgnoreCase)) continue;
            query[kv.Key] = kv.Value.ToString();
        }
        query["orderBy"] = key;
        query["dir"] = dir;
        var path = ViewContext.HttpContext.Request.Path.ToString();
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{path}?{qs}";
    }

    private static string NormalizeDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return "asc";
        return dir.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
    }

    private static string BuildSortIcon(bool isCurrent, string dir)
    {
        if (!isCurrent)
        {
            return "<svg class=\"es-data-table-sort-icon\" width=\"12\" height=\"12\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-arrow-up-down\"/></svg>";
        }
        var iconName = dir == "desc" ? "chevron-down" : "chevron-up";
        return $"<svg class=\"es-data-table-sort-icon\" width=\"12\" height=\"12\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-{iconName}\"/></svg>";
    }
}

[HtmlTargetElement("es-data-table-column", ParentTag = "es-data-table", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DataTableColumnTagHelper : TagHelper
{
    private const string SlotsKey = "es-data-table:slots";

    public string? Key { get; set; }
    public string? Label { get; set; }
    public bool Sortable { get; set; }

    /// <summary>left (default) | right | center</summary>
    public string Align { get; set; } = "left";

    public bool Numeric { get; set; }
    public bool Actions { get; set; }
    public string? Width { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is DataTableSlots slots)
        {
            slots.Columns.Add(new DataTableColumnInfo
            {
                Key = Key,
                Label = Label,
                Sortable = Sortable,
                Align = Align,
                Numeric = Numeric,
                Actions = Actions,
                Width = Width
            });
        }
        output.SuppressOutput();
    }
}

[HtmlTargetElement("es-data-table-empty", ParentTag = "es-data-table")]
public sealed class DataTableEmptyTagHelper : TagHelper
{
    private const string SlotsKey = "es-data-table:slots";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is DataTableSlots slots)
        {
            slots.EmptyHtml = content.GetContent();
        }
        output.SuppressOutput();
    }
}

[HtmlTargetElement("es-data-table-bulk-actions", ParentTag = "es-data-table")]
public sealed class DataTableBulkActionsTagHelper : TagHelper
{
    private const string SlotsKey = "es-data-table:slots";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is DataTableSlots slots)
        {
            slots.BulkActionsHtml = content.GetContent();
        }
        output.SuppressOutput();
    }
}
