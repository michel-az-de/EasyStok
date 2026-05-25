using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Form input com label, helper, error e prefix/suffix opcionais.
/// Renderiza um wrapper .es-form-field com label, input e mensagens.
///
/// Uso:
///   <es-input name="email" type="email" label="Email" required="true"
///             placeholder="seu@email.com" helper="Usado no login" />
///   <es-input name="cnpj" label="CNPJ" prefix-icon="building-2" required="true" />
///   <es-input name="valor" type="number" label="Valor" prefix="R$" />
///   <es-input name="senha" type="password" label="Senha" error="Mínimo 8 caracteres" />
/// </summary>
[HtmlTargetElement("es-input")]
public sealed class InputTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string Type { get; set; } = "text";
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
    public string? Autocomplete { get; set; }
    public string? Inputmode { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
    public string? Step { get; set; }
    public string? Pattern { get; set; }
    public int? Maxlength { get; set; }
    public int? Minlength { get; set; }

    public string? Prefix { get; set; }
    public string? Suffix { get; set; }

    [HtmlAttributeName("prefix-icon")]
    public string? PrefixIcon { get; set; }

    [HtmlAttributeName("suffix-icon")]
    public string? SuffixIcon { get; set; }

    [HtmlAttributeName("field-class")]
    public string? FieldClass { get; set; }

    [HtmlAttributeName("input-class")]
    public string? InputClass { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var fieldClasses = "es-form-field" + (string.IsNullOrEmpty(FieldClass) ? "" : " " + FieldClass);
        output.Attributes.SetAttribute("class", fieldClasses);

        var id = Id ?? Name ?? $"es-input-{Guid.NewGuid():N}".Substring(0, 16);
        var helperId = $"{id}-helper";
        var errorId = $"{id}-error";
        var hasError = !string.IsNullOrWhiteSpace(Error);

        var sb = new StringBuilder();

        // Label
        if (!string.IsNullOrWhiteSpace(Label))
        {
            var labelClasses = "es-form-label" + (Required ? " es-form-label-required" : "");
            sb.Append($"<label for=\"{WebUtility.HtmlEncode(id)}\" class=\"{labelClasses}\">");
            sb.Append(WebUtility.HtmlEncode(Label));
            if (LabelOptional && !Required)
            {
                sb.Append("<span class=\"es-form-label-optional\">(opcional)</span>");
            }
            sb.Append("</label>");
        }

        var hasGroup = !string.IsNullOrWhiteSpace(Prefix) || !string.IsNullOrWhiteSpace(Suffix)
                       || !string.IsNullOrWhiteSpace(PrefixIcon) || !string.IsNullOrWhiteSpace(SuffixIcon);

        var inputClasses = "es-form-input" + (hasError ? " is-invalid" : "") + (string.IsNullOrEmpty(InputClass) ? "" : " " + InputClass);

        if (hasGroup)
        {
            var groupClasses = "es-form-input-group" + (hasError ? " is-invalid" : "");
            sb.Append($"<div class=\"{groupClasses}\">");

            if (!string.IsNullOrWhiteSpace(PrefixIcon))
            {
                sb.Append($"<span class=\"es-form-input-prefix\" aria-hidden=\"true\">{BuildIcon(PrefixIcon, 16)}</span>");
            }
            else if (!string.IsNullOrWhiteSpace(Prefix))
            {
                sb.Append($"<span class=\"es-form-input-prefix\">{WebUtility.HtmlEncode(Prefix)}</span>");
            }

            sb.Append(BuildInput(id, inputClasses, helperId, errorId, hasError, withGroupClass: false));

            if (!string.IsNullOrWhiteSpace(SuffixIcon))
            {
                sb.Append($"<span class=\"es-form-input-suffix\" aria-hidden=\"true\">{BuildIcon(SuffixIcon, 16)}</span>");
            }
            else if (!string.IsNullOrWhiteSpace(Suffix))
            {
                sb.Append($"<span class=\"es-form-input-suffix\">{WebUtility.HtmlEncode(Suffix)}</span>");
            }

            sb.Append("</div>");
        }
        else
        {
            sb.Append(BuildInput(id, inputClasses, helperId, errorId, hasError, withGroupClass: false));
        }

        if (!string.IsNullOrWhiteSpace(Helper) && !hasError)
        {
            sb.Append($"<span id=\"{helperId}\" class=\"es-form-helper\">{WebUtility.HtmlEncode(Helper)}</span>");
        }
        if (hasError)
        {
            sb.Append($"<span id=\"{errorId}\" class=\"es-form-error\" role=\"alert\">");
            sb.Append("<svg class=\"es-icon\" width=\"12\" height=\"12\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-alert-triangle\"/></svg>");
            sb.Append(WebUtility.HtmlEncode(Error));
            sb.Append("</span>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }

    private string BuildInput(string id, string inputClasses, string helperId, string errorId, bool hasError, bool withGroupClass)
    {
        var sb = new StringBuilder();
        sb.Append($"<input id=\"{WebUtility.HtmlEncode(id)}\"");
        if (!string.IsNullOrEmpty(Name)) sb.Append($" name=\"{WebUtility.HtmlEncode(Name)}\"");
        sb.Append($" type=\"{WebUtility.HtmlEncode(Type)}\"");
        sb.Append($" class=\"{inputClasses}\"");
        if (!string.IsNullOrEmpty(Value)) sb.Append($" value=\"{WebUtility.HtmlEncode(Value)}\"");
        if (!string.IsNullOrEmpty(Placeholder)) sb.Append($" placeholder=\"{WebUtility.HtmlEncode(Placeholder)}\"");
        if (!string.IsNullOrEmpty(Autocomplete)) sb.Append($" autocomplete=\"{WebUtility.HtmlEncode(Autocomplete)}\"");
        if (!string.IsNullOrEmpty(Inputmode)) sb.Append($" inputmode=\"{WebUtility.HtmlEncode(Inputmode)}\"");
        if (Required) sb.Append(" required");
        if (Disabled) sb.Append(" disabled");
        if (Readonly) sb.Append(" readonly");
        if (Min.HasValue) sb.Append($" min=\"{Min.Value}\"");
        if (Max.HasValue) sb.Append($" max=\"{Max.Value}\"");
        if (!string.IsNullOrEmpty(Step)) sb.Append($" step=\"{WebUtility.HtmlEncode(Step)}\"");
        if (!string.IsNullOrEmpty(Pattern)) sb.Append($" pattern=\"{WebUtility.HtmlEncode(Pattern)}\"");
        if (Maxlength.HasValue) sb.Append($" maxlength=\"{Maxlength.Value}\"");
        if (Minlength.HasValue) sb.Append($" minlength=\"{Minlength.Value}\"");

        var describedBy = new List<string>();
        if (!string.IsNullOrWhiteSpace(Helper) && !hasError) describedBy.Add(helperId);
        if (hasError) describedBy.Add(errorId);
        if (describedBy.Count > 0) sb.Append($" aria-describedby=\"{string.Join(' ', describedBy)}\"");
        if (hasError) sb.Append(" aria-invalid=\"true\"");

        sb.Append(" />");
        return sb.ToString();
    }

    private static string BuildIcon(string name, int size)
    {
        var safe = WebUtility.HtmlEncode(name);
        return $"<svg class=\"es-icon\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-{safe}\"/></svg>";
    }
}
