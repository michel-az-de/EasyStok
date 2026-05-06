using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tickets;

public class DetailModel(AdminApiClient api, AdminSessionService session, ILogger<DetailModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public JsonElement TicketData { get; private set; }
    public string? Erro { get; private set; }

    public string Titulo => Str("titulo");
    public string Status => Str("status");
    public string Prioridade => Str("prioridade");
    public string Categoria => Str("categoria");
    public string EmpresaNome => Str("empresaNome");
    public string EmpresaId => Str("empresaId");
    public string AtendenteNome => Str("atendenteNome");

    public IEnumerable<JsonElement> Mensagens =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty("mensagens", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private string Str(string k) =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty(k, out var v)
        && v.ValueKind != JsonValueKind.Null ? v.GetString() ?? "" : "";

    private static readonly HashSet<string> StatusValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Aberto", "EmAtendimento", "Resolvido", "Fechado" };

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id == Guid.Empty) return NotFound();
        try
        {
            TicketData = await api.GetAsync<JsonElement>($"api/admin/tickets/{Id}");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar ticket {TicketId}", Id);
            Erro = "Não foi possível carregar o ticket. Verifique se ele ainda existe.";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostResponderAsync(string conteudo)
    {
        var conteudoT = (conteudo ?? "").Trim();
        if (conteudoT.Length is < 1 or > 8000)
        {
            SetErro("A resposta deve ter entre 1 e 8000 caracteres.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/tickets/{Id}/mensagens", new { conteudo = conteudoT });
            SetSucesso("Resposta enviada.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao responder ticket {TicketId}", Id);
            SetErro($"Falha ao enviar resposta: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostResolverAsync()
    {
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tickets/{Id}", new { status = "Resolvido" });
            SetSucesso("Ticket marcado como resolvido.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao resolver ticket {TicketId}", Id);
            SetErro($"Falha ao resolver ticket: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostAlterarStatusAsync(string status)
    {
        if (string.IsNullOrWhiteSpace(status) || !StatusValidos.Contains(status))
        {
            SetErro("Status inválido. Use Aberto, EmAtendimento, Resolvido ou Fechado.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tickets/{Id}", new { status });
            SetSucesso("Status do ticket atualizado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alterar status do ticket {TicketId} para {Status}", Id, status);
            SetErro($"Falha ao alterar status: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }
}
