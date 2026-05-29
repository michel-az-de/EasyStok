namespace EasyStock.Admin.Pages.Operacao;

/// <summary>
/// Dashboard live de operação: KPIs do dia (vendas, pedidos, caixa, devices).
/// Renderização é client-side via fetch contra <c>/api-proxy/mobile/operacao/dashboard</c>.
/// SuperAdmin escolhe empresa via <c>?empresaId=&lt;guid&gt;</c>.
/// </summary>
public class IndexModel(AdminSessionService session) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid? EmpresaId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? LojaId { get; set; }

    public void OnGet() { /* JS faz o fetch e renderiza */ }
}
