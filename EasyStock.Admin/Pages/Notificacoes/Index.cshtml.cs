namespace EasyStock.Admin.Pages.Notificacoes;

// AD-5: a rota-pai /Notificacoes não tinha Index (só as subpáginas existiam) e retornava 404
// em deep-link/refresh. Redireciona para a landing padrão da seção (Templates), preservando a
// navegação. refs #730.
public class IndexModel(AdminSessionService session) : AdminPageBase(session)
{
    public IActionResult OnGet() => RedirectToPage("/Notificacoes/Templates/Index");
}
