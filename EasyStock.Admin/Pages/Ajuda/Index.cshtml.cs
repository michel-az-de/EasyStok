namespace EasyStock.Admin.Pages.Ajuda;

/// <summary>
/// Central de Ajuda — glossário dos termos do painel. É o destino dos links
/// "Saiba mais" do &lt;es-help&gt; (/Ajuda#&lt;slug&gt;). Acessível a SuperAdmin e
/// a Admin de empresa (tenant).
/// </summary>
public class IndexModel(AdminSessionService session) : AdminPageBase(session)
{
    protected override bool PermiteNivelAdmin => true;

    public void OnGet() { }
}
