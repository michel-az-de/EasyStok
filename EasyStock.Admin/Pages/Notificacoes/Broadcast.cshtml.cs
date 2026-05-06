using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Admin.Pages.Notificacoes;

public class BroadcastModel(AdminApiClient api, AdminSessionService session)
    : AdminPageBase(session)
{
    [BindProperty] public Guid EmpresaId { get; set; }
    [BindProperty] public string UsuariosIds { get; set; } = "";
    [BindProperty] public string Titulo { get; set; } = "";
    [BindProperty] public string Mensagem { get; set; } = "";

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var ids = UsuariosIds
                .Split(['\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => Guid.TryParse(s, out _))
                .Select(Guid.Parse)
                .ToList();

            if (ids.Count == 0)
            {
                SetErro("Nenhum UsuarioId válido informado.");
                return Page();
            }

            var result = await api.PostRawAsync("api/admin/notificacoes/broadcast", new
            {
                empresaId = EmpresaId,
                usuariosDestinoIds = ids,
                titulo = Titulo,
                mensagem = Mensagem
            });

            SetSucesso($"Broadcast enviado para {ids.Count} destinatário(s).");
            return RedirectToPage();
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            SetErro(ex.Message);
            return Page();
        }
    }
}
