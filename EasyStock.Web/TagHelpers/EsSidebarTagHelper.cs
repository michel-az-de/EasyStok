using System.Text;
using System.Text.Encodings.Web;
using EasyStock.Web.Navigation;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

/// <summary>
/// Renderiza o menu lateral (ADR-0032) no servidor: resolve favoritos + badges via
/// services (cacheados) e monta o HTML via <see cref="MenuViewModelBuilder"/> puro.
/// Degrada sem derrubar a pagina (falha nos services -> seed pela flag + badges 0).
/// Acessibilidade: nav rotulado, grupos como &lt;details&gt;/&lt;summary&gt; (teclado e
/// estado aberto nativos), item ativo com aria-current, estrela como BOTAO IRMAO do
/// link (nunca aninhado em &lt;a&gt;). Interatividade (pin/accordion exclusivo/rail/
/// reorder) entra na fatia 7; este TagHelper so produz a estrutura.
/// </summary>
[HtmlTargetElement("es-sidebar")]
public sealed class EsSidebarTagHelper(
    PreferenciaMenuService favoritosSvc,
    MenuResumoService resumoSvc,
    SessionService session,
    LucideIconResolver icons,
    IConfiguration config) : TagHelper
{
    // KDS Visor abre o PWA no BROWSER -> precisa da URL PUBLICA da API (BUG-13),
    // nao da interna. Resolvido em ProcessAsync a partir de PublicApiUrl.
    private string _publicApi = string.Empty;

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        _publicApi = (config["PublicApiUrl"] ?? string.Empty).TrimEnd('/');
        var path = ViewContext?.HttpContext?.Request?.Path.Value;
        var activeMenuItem = ViewContext?.ViewData["ActiveMenuItem"] as string;
        var usuarioId = session.GetUsuarioId();
        var lojaId = session.GetLojaId();
        var empresaId = session.GetEmpresaId();

        MenuFavoritosBff fav;
        try { fav = await favoritosSvc.ObterAsync(usuarioId, lojaId); }
        catch { fav = new MenuFavoritosBff(null, false); }

        MenuBadges badges;
        try { badges = (await resumoSvc.ObterAsync(empresaId, lojaId)).Badges; }
        catch { badges = MenuBadges.Zero; }

        // Sem linha de favoritos => seed por perfil (flag KDS).
        var favoritos = fav.Favoritos ?? MenuDefinition.DefaultFavoritos(fav.KdsHabilitado);
        var vm = MenuViewModelBuilder.Build(path, activeMenuItem, favoritos, badges, fav.KdsHabilitado);

        output.TagName = "nav";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "es-nav");
        output.Attributes.SetAttribute("aria-label", "Menu principal");
        output.Attributes.SetAttribute("data-es-sidebar", "");
        output.Content.SetHtmlContent(BuildHtml(vm));
    }

    private string BuildHtml(MenuViewModel vm)
    {
        var favKeys = vm.MeuDia.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        var sb = new StringBuilder(2048);

        // Dashboard (topo fixo, fora de grupo).
        AppendItem(sb, vm.Dashboard, favKeys, idSuffix: string.Empty, showStar: false);

        // "Meu dia" (favoritos) — some quando vazio.
        if (vm.MeuDia.Count > 0)
        {
            sb.Append("<div class=\"es-fav\" data-meu-dia>");
            sb.Append("<div class=\"es-nav-label\">Meu dia</div>");
            foreach (var item in vm.MeuDia)
                AppendItem(sb, item, favKeys, idSuffix: "-fav", showStar: true);
            sb.Append("</div>");
        }

        // Grupos (accordion via <details>; aberto pela rota no load).
        foreach (var g in vm.Groups)
        {
            sb.Append("<details class=\"es-group\" data-group=\"").Append(Enc(g.Group.Key)).Append('"');
            if (g.IsOpen) sb.Append(" open");
            sb.Append('>');
            sb.Append("<summary class=\"es-group-hd\" aria-expanded=\"").Append(g.IsOpen ? "true" : "false").Append("\">");
            sb.Append(Icon("chevron-right", "es-group-chevron"));
            sb.Append(Icon(g.Group.Icon, "es-group-ico"));
            sb.Append("<span class=\"es-group-lbl\">").Append(Enc(g.Group.Label)).Append("</span>");
            sb.Append("<span class=\"es-badge es-badge--crit es-group-badge\" data-group-badge")
              .Append(g.BadgeSum > 0 ? "" : " hidden").Append('>').Append(g.BadgeSum).Append("</span>");
            sb.Append("</summary>");
            foreach (var item in g.Items)
                AppendItem(sb, item, favKeys, idSuffix: string.Empty, showStar: true);
            sb.Append("</details>");
        }

        // Rodape fixo.
        sb.Append("<div class=\"es-footer\">");
        foreach (var item in vm.Footer)
            AppendItem(sb, item, favKeys, idSuffix: string.Empty, showStar: false);
        sb.Append("</div>");

        return sb.ToString();
    }

    private void AppendItem(StringBuilder sb, MenuItemView v, HashSet<string> favKeys, string idSuffix, bool showStar)
    {
        var i = v.Item;
        sb.Append("<div id=\"es-row-").Append(Enc(i.Key)).Append(Enc(idSuffix)).Append('"')
          .Append(" class=\"es-ni-row").Append(v.IsActive ? " is-active" : "").Append('"')
          .Append(" data-menu-key=\"").Append(Enc(i.Key)).Append("\">");

        var href = i.IsExternal && _publicApi.Length > 0 ? _publicApi + i.Href : i.Href;
        sb.Append("<a class=\"es-ni\" href=\"").Append(Enc(href)).Append('"');
        if (v.IsActive) sb.Append(" aria-current=\"page\"");
        if (i.IsExternal) sb.Append(" target=\"_blank\" rel=\"noopener\"");
        sb.Append('>');

        sb.Append(Icon(i.Icon, "es-ni-ico"));
        sb.Append("<span class=\"es-ni-lbl\">").Append(Enc(i.Label)).Append("</span>");

        if (!string.IsNullOrEmpty(i.Tag))
            sb.Append("<span class=\"es-tag\">").Append(Enc(i.Tag)).Append("</span>");

        if (!string.IsNullOrEmpty(i.BadgeKey))
        {
            var cssBadge = i.BadgeKey == MenuDefinition.BadgePedidosAbertos ? "es-badge--ped" : "es-badge--crit";
            sb.Append("<span class=\"es-badge ").Append(cssBadge).Append("\" data-badge=\"").Append(Enc(i.BadgeKey)).Append('"')
              .Append(v.Badge > 0 ? "" : " hidden").Append('>').Append(v.Badge).Append("</span>");
        }
        sb.Append("</a>");

        // Estrela: botao IRMAO do link (HTML valido; nunca dentro do <a>).
        if (showStar)
        {
            var pinned = favKeys.Contains(i.Key);
            sb.Append("<button type=\"button\" class=\"es-star\" data-pin=\"").Append(Enc(i.Key)).Append('"')
              .Append(" aria-pressed=\"").Append(pinned ? "true" : "false").Append('"')
              .Append(" aria-label=\"").Append(pinned ? "Remover de Meu dia" : "Fixar em Meu dia").Append("\">");
            sb.Append(Icon("star", "es-star-ico"));
            sb.Append("</button>");
        }

        sb.Append("</div>");
    }

    private string Icon(string name, string cssClass)
    {
        var svg = icons.GetSvg(name);
        return string.IsNullOrEmpty(svg)
            ? $"<span class=\"{cssClass}\" aria-hidden=\"true\"></span>"
            : $"<span class=\"{cssClass}\" aria-hidden=\"true\">{svg}</span>";
    }

    private static string Enc(string? s) => HtmlEncoder.Default.Encode(s ?? string.Empty);
}
