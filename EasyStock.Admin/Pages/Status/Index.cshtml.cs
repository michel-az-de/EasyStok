using EasyStock.Admin.Services;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Status;

public class IndexModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    public JsonElement StatusData { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            StatusData = await api.GetAsync<JsonElement>("api/admin/status");
        }
        catch (Exception ex) { Erro = ex.Message; }
    }
}
