namespace EasyStock.Admin.Pages.Diagnostico;

/// <summary>
/// Onda 1.3 — diagnostico de email: enviar teste pelo provedor ativo (smtp|sendgrid|console).
/// Usado para validar config de SendGrid em staging/prod sem depender de evento real
/// passar pelo outbox.
/// </summary>
public class EmailModel(AdminSessionService session) : AdminPageBase(session)
{
    [BindProperty] public string Destino { get; set; } = "";
    [BindProperty] public string? Assunto { get; set; }
    [BindProperty] public string? Corpo { get; set; }

    public void OnGet() { /* form vazio */ }
}
