using EasyStock.Admin.Services;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Notificacoes.Consentimentos;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> logger)
    : AdminPageBase(session)
{
    public JsonElement? Data { get; private set; }
    public string? Erro { get; private set; }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public Guid? UsuarioId { get; set; }

    public async Task OnGetAsync()
    {
        if (!UsuarioId.HasValue) return;
        try
        {
            var result = await api.GetRawAsync($"api/admin/notificacoes/consentimentos?usuarioId={UsuarioId}");
            Data = result.GetProperty("data");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao listar consentimentos para usuario {Id}", UsuarioId);
            Erro = "Erro ao carregar consentimentos.";
        }
    }
}
