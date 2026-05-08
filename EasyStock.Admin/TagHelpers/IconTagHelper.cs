using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Renderiza um ícone do sprite Lucide em wwwroot/icons/sprite.svg.
/// Uso: <es-icon name="building-2" /> · <es-icon name="check" size="16" class="text-orange-600" />
/// Para ícones decorativos (default), aplica aria-hidden. Quando aria-label é informado,
/// vira um ícone semântico com role="img".
/// </summary>
[HtmlTargetElement("es-icon", Attributes = NameAttributeName)]
public sealed class IconTagHelper : TagHelper
{
    private const string NameAttributeName = "name";

    public string Name { get; set; } = string.Empty;
    public int Size { get; set; } = 20;
    public string? Class { get; set; }
    public double StrokeWidth { get; set; } = 1.75;

    [HtmlAttributeName("aria-label")]
    public string? AriaLabel { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;

        var classes = string.IsNullOrWhiteSpace(Class) ? "es-icon" : $"es-icon {Class}";
        output.Attributes.SetAttribute("class", classes);
        output.Attributes.SetAttribute("width", Size);
        output.Attributes.SetAttribute("height", Size);
        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
        output.Attributes.SetAttribute("fill", "none");
        output.Attributes.SetAttribute("stroke", "currentColor");
        output.Attributes.SetAttribute("stroke-width", StrokeWidth.ToString(CultureInfo.InvariantCulture));
        output.Attributes.SetAttribute("stroke-linecap", "round");
        output.Attributes.SetAttribute("stroke-linejoin", "round");

        if (string.IsNullOrWhiteSpace(AriaLabel))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
            output.Attributes.SetAttribute("focusable", "false");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", AriaLabel);
        }

        var safeName = WebUtility.HtmlEncode(Name);
        output.Content.SetHtmlContent($"<use href=\"/icons/sprite.svg#icon-{safeName}\"/>");
    }
}
