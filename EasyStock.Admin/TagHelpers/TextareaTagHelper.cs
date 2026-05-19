using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Form textarea com label, helper, error.
/// Uso: <es-textarea name="msg" label="Mensagem" rows="6" required="true" />
/// </summary>
[HtmlTargetElement("es-textarea")]
public sealed class TextareaTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Label { get; set; }

    [HtmlAttributeName("label-optional")]
    public bool LabelOptional { get; set; }

    public string? Value { get; set; }
    public string? Placeholder { get; set; }
    public string? Helper { get; set; }
    public string? Error { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }
    public bool Readonly { get; set; }
    public int Rows { get; set; } = 4;
    public int? Maxlength { get; set; }

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

        var id = Id ?? Name ?? $"es-ta-{Guid.NewGuid():N}".Substring(0, 12);
        var helperId = $"{id}-helper";
        var errorId = $"{id}-error";
        var hasError = !string.IsNullOrWhiteSpace(Error);

        // children = valor inicial alternativo (se Value não foi setado)
        var children = await output.GetChildContentAsync();
        var fallbackValue = string.IsNullOrEmpty(Value) ? children.GetContent() : Value;

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

        var inputClasses = "es-form-textarea" + (hasError ? " is-invalid" : "") + (string.IsNullOrEmpty(InputClass) ? "" : " " + InputClass);
        sb.Append($"<textarea id=\"{WebUtility.HtmlEncode(id)}\"");
        if (!string.IsNullOrEmpty(Name)) sb.Append($" name=\"{WebUtility.HtmlEncode(Name)}\"");
        sb.Append($" class=\"{inputClasses}\"");
        sb.Append($" rows=\"{Rows}\"");
        if (!string.IsNullOrEmpty(Placeholder)) sb.Append($" placeholder=\"{WebUtility.HtmlEncode(Placeholder)}\"");
        if (Required) sb.Append(" required");
        if (Disabled) sb.Append(" disabled");
        if (Readonly) sb.Append(" readonly");
        if (Maxlength.HasValue) sb.Append($" maxlength=\"{Maxlength.Value}\"");

        var describedBy = new List<string>();
        if (!string.IsNullOrWhiteSpace(Helper) && !hasError) describedBy.Add(helperId);
        if (hasError) describedBy.Add(errorId);
        if (describedBy.Count > 0) sb.Append($" aria-describedby=\"{string.Join(' ', describedBy)}\"");
        if (hasError) sb.Append(" aria-invalid=\"true\"");

        sb.Append('>');
        sb.Append(WebUtility.HtmlEncode(fallbackValue ?? string.Empty));
        sb.Append("</textarea>");

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
