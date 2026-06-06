using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Botão de copiar-para-a-área-de-transferência com feedback via toast.
/// Renderiza o valor (opcional) ao lado de um botão que copia o <c>value</c> cru.
/// Reusa a factory Alpine <c>esCopy()</c> (wwwroot/js/admin-components.js) e o store
/// global de toast. Para valores dinâmicos (x-text), use <c>esCopy()</c> direto no markup.
///
/// Uso:
///   &lt;es-copy value="@Model.Cnpj" label="CNPJ" /&gt;
///   &lt;es-copy value="@id.ToString()" label="ID" icon-only="true" /&gt;
///   &lt;es-copy value="@email" label="E-mail" text="@email" /&gt;
/// </summary>
[HtmlTargetElement("es-copy", Attributes = "value")]
[HtmlTargetElement("es-copy", Attributes = "expr")]
public sealed class CopyTagHelper : TagHelper
{
    /// <summary>Valor cru a copiar (estático). Use isto OU expr.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Expressão Alpine a copiar (ex.: "d.id"), avaliada no escopo Alpine. Alternativa a value para valores dinâmicos (x-for, x-text).</summary>
    public string? Expr { get; set; }

    /// <summary>Rótulo humano para o toast ("CNPJ copiado"). Opcional.</summary>
    public string? Label { get; set; }

    /// <summary>Texto visível ao lado do botão. Default: o próprio value.</summary>
    public string? Text { get; set; }

    /// <summary>Quando true, mostra só o botão (sem o valor ao lado).</summary>
    [HtmlAttributeName("icon-only")]
    public bool IconOnly { get; set; }

    /// <summary>Exibe o valor em fonte monoespaçada (default true).</summary>
    public bool Mono { get; set; } = true;

    /// <summary>Tamanho do ícone em px (default 14).</summary>
    public int Size { get; set; } = 14;

    [HtmlAttributeName("class")]
    public string? Class { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        var wrapperClass = "es-copy" + (string.IsNullOrWhiteSpace(Class) ? "" : " " + Class);
        output.Attributes.SetAttribute("class", wrapperClass);
        output.Attributes.SetAttribute("x-data", "esCopy()");

        var value = Value ?? string.Empty;
        var hasExpr = !string.IsNullOrWhiteSpace(Expr);
        var visible = Text ?? (hasExpr ? null : value);
        var actionLabel = string.IsNullOrWhiteSpace(Label) ? "Copiar" : "Copiar " + Label;

        // Expressão Alpine do @click. value/label viram string JS (JsonSerializer); expr é
        // injetada crua (já é expressão Alpine, ex.: d.id). O conjunto é HTML-encodado: as
        // aspas viram &quot; e o browser decodifica antes do Alpine avaliar.
        var jsLabel = string.IsNullOrWhiteSpace(Label) ? "null" : System.Text.Json.JsonSerializer.Serialize(Label);
        var copyArg = hasExpr ? Expr! : System.Text.Json.JsonSerializer.Serialize(value);
        var clickExpr = WebUtility.HtmlEncode($"copy({copyArg}, {jsLabel})");
        var titleAttr = WebUtility.HtmlEncode(actionLabel);

        var sb = new StringBuilder();

        if (!IconOnly && !string.IsNullOrEmpty(visible))
        {
            var textClass = "es-copy-text" + (Mono ? "" : " es-copy-text-plain");
            sb.Append($"<span class=\"{textClass}\">{WebUtility.HtmlEncode(visible)}</span>");
        }

        sb.Append($"<button type=\"button\" class=\"es-copy-btn\" @click=\"{clickExpr}\" title=\"{titleAttr}\" aria-label=\"{titleAttr}\">");
        sb.Append(CopyIcon());
        sb.Append(CheckIcon());
        sb.Append("</button>");

        output.Content.SetHtmlContent(sb.ToString());
    }

    private string CopyIcon() =>
        $"<svg class=\"es-copy-ico\" x-show=\"!copied\" width=\"{Size}\" height=\"{Size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">"
        + "<rect width=\"14\" height=\"14\" x=\"8\" y=\"8\" rx=\"2\" ry=\"2\"/>"
        + "<path d=\"M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2\"/></svg>";

    private string CheckIcon() =>
        $"<svg class=\"es-copy-ico es-copy-ico-check\" x-show=\"copied\" x-cloak width=\"{Size}\" height=\"{Size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">"
        + "<path d=\"M20 6 9 17l-5-5\"/></svg>";
}
