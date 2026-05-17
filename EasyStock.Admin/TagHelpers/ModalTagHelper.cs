using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

internal sealed class ModalSlots
{
    public string? FooterHtml { get; set; }
}

/// <summary>
/// Modal accessibility-aware com transições, focus trap e slot footer.
/// Abertura via window event:
///     window.dispatchEvent(new CustomEvent('open-modal-{id}'))
///   ou Alpine helper:
///     $dispatch('open-modal-{id}')
///
/// Uso:
///   <es-modal id="suspender" title="Suspender cliente"
///             description="Esta ação pode ser revertida.">
///       <es-textarea name="motivo" required="true" rows="3" label="Motivo" />
///       <es-modal-footer>
///           <es-button variant="ghost" x-on:click="close()">Cancelar</es-button>
///           <es-button variant="danger" type="submit">Confirmar</es-button>
///       </es-modal-footer>
///   </es-modal>
/// </summary>
[HtmlTargetElement("es-modal")]
public sealed class ModalTagHelper : TagHelper
{
    private const string SlotsKey = "es-modal:slots";

    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>sm | md (default) | lg | xl</summary>
    public string Size { get; set; } = "md";

    /// <summary>Quando true, o modal não fecha ao clicar no backdrop nem no Esc.</summary>
    public bool Persistent { get; set; }

    [HtmlAttributeName("show-close")]
    public bool ShowCloseButton { get; set; } = true;

    public override void Init(TagHelperContext context)
    {
        context.Items[SlotsKey] = new ModalSlots();
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var id = Id ?? $"modal-{Guid.NewGuid():N}"[..12];
        var titleId = $"{id}-title";
        var descId = $"{id}-desc";
        var safeId = WebUtility.HtmlEncode(id);

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("id", id);
        output.Attributes.SetAttribute("class", "es-modal-overlay");
        output.Attributes.SetAttribute("x-data", "{ open: false, entered: false, close() { this.entered = false; setTimeout(() => { this.open = false; document.body.style.overflow = ''; }, 180); } }");
        output.Attributes.SetAttribute("x-show", "open");
        output.Attributes.SetAttribute("x-cloak", string.Empty);
        output.Attributes.SetAttribute("role", "dialog");
        output.Attributes.SetAttribute("aria-modal", "true");
        if (!string.IsNullOrWhiteSpace(Title)) output.Attributes.SetAttribute("aria-labelledby", titleId);
        if (!string.IsNullOrWhiteSpace(Description)) output.Attributes.SetAttribute("aria-describedby", descId);
        if (!Persistent)
        {
            output.Attributes.SetAttribute("@click.self", "close()");
            output.Attributes.SetAttribute("@keydown.escape.window", "if (open) close()");
        }
        output.Attributes.SetAttribute($"@open-modal-{id}.window",
            "open = true; document.body.style.overflow = 'hidden'; $nextTick(() => entered = true); $nextTick(() => $el.querySelector('.es-modal-panel')?.focus())");
        output.Attributes.SetAttribute($"@close-modal-{id}.window", "close()");

        var content = await output.GetChildContentAsync();
        var body = content.GetContent() ?? string.Empty;

        var slots = (context.Items.TryGetValue(SlotsKey, out var v) && v is ModalSlots s) ? s : new ModalSlots();

        var sb = new StringBuilder();
        sb.Append($"<div class=\"es-modal-panel es-modal-{Size}\" :class=\"entered ? 'is-entered' : ''\" tabindex=\"-1\" x-trap.inert.noscroll=\"open\">");

        if (!string.IsNullOrWhiteSpace(Title) || ShowCloseButton)
        {
            sb.Append("<header class=\"es-modal-header\">");
            sb.Append("<div>");
            if (!string.IsNullOrWhiteSpace(Title))
                sb.Append($"<h2 id=\"{titleId}\" class=\"es-modal-title\">{WebUtility.HtmlEncode(Title)}</h2>");
            if (!string.IsNullOrWhiteSpace(Description))
                sb.Append($"<p id=\"{descId}\" class=\"es-modal-desc\">{WebUtility.HtmlEncode(Description)}</p>");
            sb.Append("</div>");
            if (ShowCloseButton)
            {
                sb.Append("<button type=\"button\" class=\"es-modal-close\" @click=\"close()\" aria-label=\"Fechar\">");
                sb.Append("<svg class=\"es-icon\" width=\"18\" height=\"18\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><use href=\"/icons/sprite.svg#icon-x\"/></svg>");
                sb.Append("</button>");
            }
            sb.Append("</header>");
        }

        sb.Append("<div class=\"es-modal-body\">");
        sb.Append(body);
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(slots.FooterHtml))
        {
            sb.Append("<footer class=\"es-modal-footer\">");
            sb.Append(slots.FooterHtml);
            sb.Append("</footer>");
        }

        sb.Append("</div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

[HtmlTargetElement("es-modal-footer", ParentTag = "es-modal")]
public sealed class ModalFooterTagHelper : TagHelper
{
    private const string SlotsKey = "es-modal:slots";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is ModalSlots slots)
        {
            slots.FooterHtml = content.GetContent();
        }
        output.SuppressOutput();
    }
}
