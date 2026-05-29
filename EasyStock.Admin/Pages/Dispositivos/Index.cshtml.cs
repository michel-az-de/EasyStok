namespace EasyStock.Admin.Pages.Dispositivos;

/// <summary>
/// Lista os PWAs pareados de uma empresa, mostra saúde, gera novos
/// pair-codes e enfileira comandos remotos (incluindo o "pwa_update"
/// que força atualização OTA do bundle servido em /pwa/).
///
/// SuperAdmin escolhe a empresa via <c>?empresaId=&lt;guid&gt;</c>.
/// O backend (<c>DevicePairingController</c>) já isola por tenant, então a
/// query string é só um filtro de UI.
/// </summary>
public class IndexModel(AdminSessionService session) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid? EmpresaId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? LojaId { get; set; }

    public void OnGet() { /* tudo carregado via JS pelos proxies /api-proxy/mobile/* */ }
}
