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
    public string Descricao => Str("descricao");
    public string Status => Str("status");
    public string Prioridade => Str("prioridade");
    public string Categoria => Str("categoria");
    public string Nivel => Str("nivel");
    public string EmpresaNome => Str("empresaNome");
    public string EmpresaId => Str("empresaId");
    public string AtendenteNome => Str("atendenteNome");

    public DateTime? PrazoResposta => Date("prazoResposta");
    public DateTime? PrazoResolucao => Date("prazoResolucao");
    public DateTime? PrimeiraRespostaEm => Date("primeiraRespostaEm");
    public DateTime? ResolvidoEm => Date("resolvidoEm");
    public bool SlaRespostaViolado => Bool("slaRespostaViolado");
    public bool SlaResolucaoViolado => Bool("slaResolucaoViolado");
    public string? OrigemTicketId => Str("origemTicketId");
    public string? OrigemTicketTitulo => Str("origemTicketTitulo");

    public IEnumerable<JsonElement> Mensagens =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty("mensagens", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public IEnumerable<JsonElement> Anexos =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty("anexos", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public IEnumerable<JsonElement> Historico =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty("historico", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public JsonElement? MetaTecnico
    {
        get
        {
            if (TicketData.ValueKind == JsonValueKind.Undefined) return null;
            if (!TicketData.TryGetProperty("metaTecnico", out var v) || v.ValueKind == JsonValueKind.Null) return null;
            return v;
        }
    }

    private string Str(string k) =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty(k, out var v)
        && v.ValueKind != JsonValueKind.Null ? v.GetString() ?? "" : "";

    private bool Bool(string k) =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty(k, out var v)
        && v.ValueKind == JsonValueKind.True;

    private DateTime? Date(string k)
    {
        if (TicketData.ValueKind == JsonValueKind.Undefined) return null;
        if (!TicketData.TryGetProperty(k, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return DateTime.TryParse(v.GetString(), out var dt) ? dt : null;
    }

    private static readonly HashSet<string> StatusValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Aberto", "EmAtendimento", "AguardandoCliente", "Resolvido", "Fechado" };
    private static readonly HashSet<string> NiveisValidos = new(StringComparer.OrdinalIgnoreCase)
        { "N1", "N2", "N3", "N4" };

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

    public async Task<IActionResult> OnPostResponderAsync(string conteudo, bool interno = false)
    {
        var conteudoT = (conteudo ?? "").Trim();
        if (conteudoT.Length is < 1 or > 8000)
        {
            SetErro("A resposta deve ter entre 1 e 8000 caracteres.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/tickets/{Id}/mensagens", new { conteudo = conteudoT, interno });
            SetSucesso(interno ? "Comentário interno adicionado." : "Resposta enviada.");
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
            SetErro("Status inválido.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tickets/{Id}", new { status });
            SetSucesso("Status atualizado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alterar status do ticket {TicketId}", Id);
            SetErro($"Falha ao alterar status: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostAssumirAsync()
    {
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/tickets/{Id}/assumir", new { });
            SetSucesso("Ticket assumido.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao assumir ticket {TicketId}", Id);
            SetErro($"Falha ao assumir: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostEncaminharAsync(string novoNivel, string? motivo)
    {
        if (string.IsNullOrWhiteSpace(novoNivel) || !NiveisValidos.Contains(novoNivel))
        {
            SetErro("Nível inválido.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/tickets/{Id}/encaminhar", new { novoNivel, motivo });
            SetSucesso($"Ticket encaminhado para {novoNivel}.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao encaminhar ticket {TicketId}", Id);
            SetErro($"Falha ao encaminhar: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostBugFixAsync(string titulo, string descricao, string severidade, string? componente, string? stackTrace)
    {
        var tT = (titulo ?? "").Trim();
        var dT = (descricao ?? "").Trim();
        if (tT.Length is < 3 or > 200 || dT.Length is < 5 or > 4000)
        {
            SetErro("Título (3-200) e descrição (5-4000) obrigatórios.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/tickets/{Id}/bug-fix",
                new { titulo = tT, descricao = dT, severidade = severidade ?? "Media", componente, stackTrace });
            SetSucesso("Bug-fix encaminhado para o time de desenvolvimento.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao gerar bug-fix do ticket {TicketId}", Id);
            SetErro($"Falha ao gerar bug-fix: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }
}
