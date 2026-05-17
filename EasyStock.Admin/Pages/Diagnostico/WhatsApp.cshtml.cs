using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Admin.Pages.Diagnostico;

/// <summary>
/// Onda 2.1 — diagnostico de WhatsApp Business (Meta Cloud). Envia mensagem de teste
/// em modo text ou template. Modo template eh o que importa fora da janela 24h —
/// requer template aprovado previamente na Meta Business Manager.
/// </summary>
public class WhatsAppModel(AdminSessionService session) : AdminPageBase(session)
{
    public void OnGet() { /* form vazio */ }
}
