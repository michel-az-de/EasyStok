using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;
using EasyStock.Admin.Glossario;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Gatilho de ajuda contextual ("?") com popover pedagógico, acessível via
/// teclado e mouse. Abre em hover E foco; fecha em Esc e clique-fora.
///
/// Modos de uso:
///   1. Verbete do glossário (atributo term):
///        &lt;es-help term="trial" /&gt;
///      Busca GlossarioTermos.TryGet(term) e usa Termo (título), Curto (texto)
///      e Ancora (link "Saiba mais" -&gt; /Ajuda#&lt;ancora&gt;).
///
///   2. Ajuda livre (conteúdo-filho):
///        &lt;es-help title="MRR"&gt;Receita recorrente mensal das assinaturas ativas.&lt;/es-help&gt;
///
///   3. Verbete + complemento livre (conteúdo-filho aparece abaixo do verbete):
///        &lt;es-help term="sla-nivel-atendimento"&gt;Veja a matriz em Configurações.&lt;/es-help&gt;
///
/// A11y: o gatilho expõe aria-expanded e aponta (aria-controls/aria-describedby)
/// para o id único do popover. O foco volta ao gatilho ao fechar com Esc.
///
/// A renderização vive em <see cref="HelpMarkup"/>, reutilizada por outros
/// TagHelpers (ex.: es-page-header com help-term).
/// </summary>
[HtmlTargetElement("es-help")]
public sealed class HelpTagHelper : TagHelper
{
    /// <summary>Slug do verbete no glossário (opcional).</summary>
    public string? Term { get; set; }

    /// <summary>Título do popover quando não há verbete (opcional).</summary>
    public string? Title { get; set; }

    /// <summary>Rótulo do botão para leitores de tela. Default: "Ajuda".</summary>
    [HtmlAttributeName("aria-label")]
    public string? AriaLabel { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Conteúdo-filho = ajuda livre (Razor já codifica).
        var child = await output.GetChildContentAsync();
        var freeHtml = child.GetContent() ?? string.Empty;

        // Não envolve em elemento extra: emitimos o nosso próprio <span class="es-help">.
        output.TagName = null;
        output.Content.SetHtmlContent(HelpMarkup.Render(Term, Title, freeHtml, AriaLabel));
    }
}

/// <summary>
/// Renderiza a marcação do gatilho de ajuda contextual (es-help) como string HTML.
/// Compartilhado por <see cref="HelpTagHelper"/> e por outros TagHelpers que embutem
/// ajuda inline (ex.: es-page-header), porque TagHelpers aninhados emitidos via
/// SetHtmlContent não são re-processados pelo Razor — mas o Alpine (x-data) hidrata
/// a marcação no cliente normalmente.
/// </summary>
internal static class HelpMarkup
{
    /// <summary>
    /// Monta o &lt;span class="es-help"&gt; com gatilho + popover.
    /// </summary>
    /// <param name="term">Slug do verbete no glossário (opcional).</param>
    /// <param name="title">Título quando não há verbete (opcional).</param>
    /// <param name="freeHtml">HTML de ajuda livre, já codificado pelo Razor (opcional).</param>
    /// <param name="ariaLabel">Rótulo acessível do gatilho (opcional; default "Ajuda").</param>
    public static string Render(string? term, string? title, string? freeHtml, string? ariaLabel)
    {
        string? tituloVerbete = null;
        string? textoVerbete = null;
        string? ancora = null;
        if (!string.IsNullOrWhiteSpace(term) && GlossarioTermos.TryGet(term, out var verbete))
        {
            tituloVerbete = verbete.Termo;
            textoVerbete = verbete.Curto;
            ancora = verbete.Ancora;
        }

        // Título exibido: verbete > atributo title > nenhum.
        var titulo = !string.IsNullOrWhiteSpace(tituloVerbete) ? tituloVerbete : title;

        var panelId = $"es-help-{System.Guid.NewGuid():N}";
        var label = string.IsNullOrWhiteSpace(ariaLabel)
            ? (string.IsNullOrWhiteSpace(titulo) ? "Ajuda" : $"Ajuda sobre {titulo}")
            : ariaLabel;

        var sb = new StringBuilder();

        // Wrapper que hospeda o estado Alpine do par gatilho+popover.
        sb.Append("<span class=\"es-help\" x-data=\"esHelp()\" @keydown.escape.stop=\"onKeydown($event)\">");

        // ── Gatilho ──────────────────────────────────────────────
        sb.Append("<button type=\"button\" class=\"es-help-trigger\"");
        sb.Append($" aria-label=\"{WebUtility.HtmlEncode(label)}\"");
        sb.Append(" :aria-expanded=\"open ? 'true' : 'false'\"");
        sb.Append($" aria-controls=\"{panelId}\" aria-describedby=\"{panelId}\"");
        sb.Append(" x-ref=\"trigger\"");
        sb.Append(" @click=\"toggle()\"");
        sb.Append(" @mouseenter=\"show()\" @mouseleave=\"scheduleHide()\"");
        sb.Append(" @focus=\"show()\" @blur=\"scheduleHide()\">");
        sb.Append("<svg class=\"es-icon\" width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" ");
        sb.Append("fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" ");
        sb.Append("stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">");
        sb.Append("<use href=\"/icons/sprite.svg#icon-info\"/></svg>");
        sb.Append("</button>");

        // ── Popover ──────────────────────────────────────────────
        sb.Append($"<span class=\"es-help-popover\" id=\"{panelId}\" role=\"tooltip\"");
        sb.Append(" x-show=\"open\" x-cloak");
        sb.Append(" @mouseenter=\"show()\" @mouseleave=\"scheduleHide()\">");

        if (!string.IsNullOrWhiteSpace(titulo))
        {
            sb.Append($"<span class=\"es-help-title\">{WebUtility.HtmlEncode(titulo)}</span>");
        }

        sb.Append("<span class=\"es-help-body\">");
        if (!string.IsNullOrWhiteSpace(textoVerbete))
        {
            sb.Append($"<span class=\"es-help-text\">{WebUtility.HtmlEncode(textoVerbete)}</span>");
        }
        if (!string.IsNullOrWhiteSpace(freeHtml))
        {
            // Conteúdo-filho já vem codificado/validado pelo Razor.
            sb.Append($"<span class=\"es-help-text\">{freeHtml}</span>");
        }
        sb.Append("</span>");

        if (!string.IsNullOrWhiteSpace(ancora))
        {
            var safeAncora = WebUtility.HtmlEncode(ancora);
            sb.Append($"<a class=\"es-help-more\" href=\"/Ajuda#{safeAncora}\">Saiba mais</a>");
        }

        sb.Append("</span>");

        return sb.ToString();
    }
}
