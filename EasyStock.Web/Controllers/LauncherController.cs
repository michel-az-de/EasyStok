using EasyStock.Web.Helpers;
using EasyStock.Web.Navigation;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Portal de módulos (ADR-0046): tela de entrada pós-login que apresenta os módulos como
/// cards acionáveis, com o pulso do dia e o "Meu dia". Substitui o Dashboard como home
/// autenticada — que continua existindo como âncora, visível dentro de qualquer módulo.
///
/// <para>
/// Casca fina: busca os dados (pelos mesmos serviços cacheados que o menu lateral usa) e
/// delega a montagem ao <see cref="LauncherViewModelBuilder"/>, que é puro e testável.
/// </para>
/// </summary>
public class LauncherController(
    MenuResumoService resumoSvc,
    PreferenciaMenuService favoritosSvc,
    SessionService session) : BaseController(session)
{
    // NAO reivindicar "/": a raiz e da landing publica (SiteController), que redireciona
    // pra ca quando ha sessao. Dois attribute routes com o mesmo template derrubariam
    // GET / com AmbiguousMatchException, inclusive para visitante anonimo.
    [HttpGet("/launcher")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Portal";
        ViewBag.ActiveMenuItem = "Launcher";

        var usuarioId = Session.GetUsuarioId();
        var lojaId = Session.GetLojaId();
        var empresaId = Session.GetEmpresaId();

        // Mesmos serviços do <es-sidebar>: cache de 60s (resumo) e 5min (favoritos)
        // compartilhado, então abrir o portal não dispara requests a mais.
        // Degrada como o menu: falha vira resumo vazio / seed conservador.
        MenuResumoRaw resumo;
        try { resumo = await resumoSvc.ObterRawAsync(empresaId, lojaId); }
        catch { resumo = new MenuResumoRaw(null, null, Ok: false); }

        MenuFavoritosBff fav;
        try { fav = await favoritosSvc.ObterAsync(usuarioId, lojaId); }
        catch { fav = new MenuFavoritosBff(null, false); }

        var badges = resumo.Dash is null
            ? MenuBadges.Zero
            : new MenuBadges(
                PedidosAbertos: resumo.Dia?.PedidosPendentes ?? 0,
                ProdutosCriticos: resumo.Dash.AlertasEstoqueBaixo,
                LotesVencidos: resumo.Dash.AlertasVencidos);

        // "Meu dia" real: mesma resolução do menu (seed por perfil quando não há linha
        // salva), com a flag KDS do tenant em vez do true hardcoded.
        var favoritos = fav.Favoritos ?? MenuDefinition.DefaultFavoritos(fav.KdsHabilitado);
        var menu = MenuViewModelBuilder.Build(
            currentPath: "/launcher", activeMenuItem: null,
            favoritos, badges, fav.KdsHabilitado);

        var vm = LauncherViewModelBuilder.Montar(
            BrazilTime.Now(),
            Session.GetUsuarioNome(),
            resumo.Dash,
            resumo.Dia,
            badges,
            menu.MeuDia,
            ModuloDefinition.Modulos);

        return View(vm);
    }
}
