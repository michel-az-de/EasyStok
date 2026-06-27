using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Cabeçalho pedagógico de página. Padroniza a abertura de toda tela do Admin com a
/// hierarquia do deck: eyebrow (mono uppercase), título (Fraunces) e descrição que
/// explica o que a tela faz. Slot de ações (conteúdo-filho) cai à direita.
///
/// Compõe — mas NÃO recria — &lt;es-breadcrumb&gt;: passe-o no slot &lt;es-page-header-breadcrumb&gt;
/// (renderizado acima do eyebrow) ou simplesmente antes deste elemento na página.
///
/// Quando help-term é informado, embute o gatilho de ajuda (&lt;es-help&gt;) ao lado do
/// título reutilizando <see cref="HelpMarkup"/> — o popover do glossário funciona igual.
///
/// Uso:
///   &lt;es-page-header eyebrow="Faturamento"
///                   titulo="Faturas"
///                   descricao="Documentos de cobrança de assinaturas, pedidos e avulsos."
///                   help-term="fatura"&gt;
///       &lt;es-button variant="primary" icon="plus"&gt;Nova fatura&lt;/es-button&gt;
///   &lt;/es-page-header&gt;
///
/// Com breadcrumb no slot:
///   &lt;es-page-header titulo="Acme Corp"&gt;
///       &lt;es-page-header-breadcrumb&gt;
///           &lt;es-breadcrumb&gt;
///               &lt;es-breadcrumb-item href="/"&gt;Início&lt;/es-breadcrumb-item&gt;
///               &lt;es-breadcrumb-item href="/Tenants"&gt;Clientes&lt;/es-breadcrumb-item&gt;
///               &lt;es-breadcrumb-item&gt;Acme Corp&lt;/es-breadcrumb-item&gt;
///           &lt;/es-breadcrumb&gt;
///       &lt;/es-page-header-breadcrumb&gt;
///   &lt;/es-page-header&gt;
/// </summary>
[HtmlTargetElement("es-page-header")]
public sealed class PageHeaderTagHelper : TagHelper
{
    private const string SlotsKey = "es-page-header:slots";

    /// <summary>Linha mono uppercase acima do título (ex.: área/módulo). Opcional.</summary>
    public string? Eyebrow { get; set; }

    /// <summary>Título principal da página (Fraunces). Obrigatório na prática.</summary>
    public string? Titulo { get; set; }

    /// <summary>Frase pedagógica curta que explica o que a tela faz. Opcional.</summary>
    public string? Descricao { get; set; }

    /// <summary>
    /// Slug de termo do glossário. Quando presente, embute o gatilho de ajuda
    /// (es-help) ao lado do título via <see cref="HelpMarkup"/>.
    /// </summary>
    [HtmlAttributeName("help-term")]
    public string? HelpTerm { get; set; }

    public override void Init(TagHelperContext context)
    {
        context.Items[SlotsKey] = new PageHeaderSlots();
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "header";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-page-header");

        // Filhos rodam aqui dentro: o slot de breadcrumb é populado e o restante
        // do conteúdo-filho vira o slot de ações.
        var content = await output.GetChildContentAsync();
        var actionsHtml = content.GetContent() ?? string.Empty;

        var slots = (context.Items.TryGetValue(SlotsKey, out var v) && v is PageHeaderSlots s)
            ? s
            : new PageHeaderSlots();

        var sb = new StringBuilder();

        // Breadcrumb (slot) acima de tudo — componente existente, processado normalmente.
        if (!string.IsNullOrWhiteSpace(slots.BreadcrumbHtml))
        {
            sb.Append("<div class=\"es-page-header-breadcrumb\">");
            sb.Append(slots.BreadcrumbHtml);
            sb.Append("</div>");
        }

        sb.Append("<div class=\"es-page-header-row\">");

        // Coluna de texto: eyebrow + (título + help) + descrição.
        sb.Append("<div class=\"es-page-header-text\">");

        if (!string.IsNullOrWhiteSpace(Eyebrow))
        {
            sb.Append("<p class=\"es-page-header-eyebrow\">");
            sb.Append(WebUtility.HtmlEncode(Eyebrow));
            sb.Append("</p>");
        }

        sb.Append("<div class=\"es-page-header-title-line\">");
        sb.Append("<h1 class=\"es-page-header-title\">");
        sb.Append(WebUtility.HtmlEncode(Titulo ?? string.Empty));
        sb.Append("</h1>");

        if (!string.IsNullOrWhiteSpace(HelpTerm))
        {
            // Reutiliza o gatilho de ajuda real (glossário + popover acessível).
            sb.Append(HelpMarkup.Render(HelpTerm, title: null, freeHtml: null, ariaLabel: null));
        }

        sb.Append("</div>"); // .es-page-header-title-line

        if (!string.IsNullOrWhiteSpace(Descricao))
        {
            sb.Append("<p class=\"es-page-header-desc\">");
            sb.Append(WebUtility.HtmlEncode(Descricao));
            sb.Append("</p>");
        }

        sb.Append("</div>"); // .es-page-header-text

        // Slot de ações (conteúdo-filho que sobrou) à direita.
        if (!string.IsNullOrWhiteSpace(actionsHtml))
        {
            sb.Append("<div class=\"es-page-header-actions\">");
            sb.Append(actionsHtml);
            sb.Append("</div>");
        }

        sb.Append("</div>"); // .es-page-header-row

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>
/// Slot opcional para encaixar &lt;es-breadcrumb&gt; acima do cabeçalho, sem recriá-lo.
/// O conteúdo-filho é processado normalmente (TagHelpers aninhados rodam).
/// </summary>
[HtmlTargetElement("es-page-header-breadcrumb", ParentTag = "es-page-header")]
public sealed class PageHeaderBreadcrumbTagHelper : TagHelper
{
    private const string SlotsKey = "es-page-header:slots";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(SlotsKey, out var v) && v is PageHeaderSlots slots)
        {
            slots.BreadcrumbHtml = content.GetContent();
        }
        output.SuppressOutput();
    }
}

internal sealed class PageHeaderSlots
{
    public string? BreadcrumbHtml { get; set; }
}
