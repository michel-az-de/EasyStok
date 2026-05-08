using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Form select. Children são <option> normais.
/// Uso:
///   <es-select name="status" label="Status" required="true">
///       <option value="">Todos</option>
///       <option value="ativa">Ativas</option>
///   </es-select>
/// </summary>
[HtmlTargetElement("es-select")]
public sealed class SelectTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Label { get; set; }

    [HtmlAttributeName("label-optional")]
    public bool LabelOptional { get; set; }

    public string? Helper { get; set; }
    public string? Error { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }
    public bool Multiple { get; set; }

    [HtmlAttributeName("field-class")]
    public string? FieldClass { get; set; }

    [HtmlAttributeName("input-class")]
    public string? InputClass { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var fieldClasses = "es-form-field" + (string.IsNullOrEmpty(FieldClass) ? "" : " " + FieldClass);
        output.Attributes.SetAttribute("class", fieldClasses);

        var id = Id ?? Name ?? $"es-sel-{Guid.NewGuid():N}".Substring(0, 12);
        var helperId = $"{id}-helper";
        var errorId = $"{id}-error";
        var hasError = !string.IsNullOrWhiteSpace(Error);

        var content = await output.GetChildContentAsync();
        var optionsHtml = content.GetContent() ?? string.Empty;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Label))
        {
            var labelClasses = "es-form-label" + (Required ? " es-form-label-required" : "");
            sb.Append($"<label for=\"{WebUtility.HtmlEncode(id)}\" class=\"{labelClasses}\">");
            sb.Append(WebUtility.HtmlEncode(Label));
            if (LabelOptional && !Required)
                sb.Append("<span class=\"es-form-label-optional\">(opcional)</span>");
            sb.Append("</label>");
        }

        var inputClasses = "es-form-select" + (hasError ? " is-invalid" : "") + (string.IsNullOrEmpty(InputClass) ? "" : " " + InputClass);
        sb.Append($"<select id=\"{WebUtility.HtmlEncode(id)}\"");
        if (!string.IsNullOrEmpty(Name)) sb.Append($" name=\"{WebUtility.HtmlEncode(Name)}\"");
        sb.Append($" class=\"{inputClasses}\"");
        if (Required) sb.Append(" required");
        if (Disabled) sb.Append(" disabled");
        if (Multiple) sb.Append(" multiple");

        var describedBy = new List<string>();
        if (!string.IsNullOrWhiteSpace(Helper) && !hasError) describedBy.Add(helperId);
        if (hasError) describedBy.Add(errorId);
        if (describedBy.Count > 0) sb.Append($" aria-describedby=\"{string.Join(' ', describedBy)}\"");
        if (hasError) sb.Append(" aria-invalid=\"true\"");

        sb.Append('>');
        sb.Append(optionsHtml);
        sb.Append("</select>");

        if (!string.IsNullOrWhiteSpace(Helper) && !hasError)
            sb.Append($"<span id=\"{helperId}\" class=\"es-form-helper\">{WebUtility.HtmlEncode(Helper)}</span>");
        if (hasError)
        {
            sb.Append($"<span id=\"{errorId}\" class=\"es-form-error\" role=\"alert\">");
            sb.Append("<svg class=\"es-icon\" width=\"12\" height=\"12\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-alert-triangle\"/></svg>");
            sb.Append(WebUtility.HtmlEncode(Error));
            sb.Append("</span>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }
}
