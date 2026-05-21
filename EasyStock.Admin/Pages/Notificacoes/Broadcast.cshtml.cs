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
        var tituloT = Titulo.Trim();
        var mensagemT = Mensagem.Trim();

        if (EmpresaId == Guid.Empty) { SetErro("Selecione uma empresa."); return Page(); }
        if (tituloT.Length is < 3 or > 200) { SetErro("Título deve ter entre 3 e 200 caracteres."); return Page(); }
        if (mensagemT.Length is < 5 or > 2000) { SetErro("Mensagem deve ter entre 5 e 2000 caracteres."); return Page(); }

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
                titulo = tituloT,
                mensagem = mensagemT
            });

            SetSucesso($"Broadcast enviado para {ids.Count} destinatário(s).");
            return RedirectToPage();
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            SetErroSeguro(ex, "Enviar broadcast");
            return Page();
        }
    }
}
