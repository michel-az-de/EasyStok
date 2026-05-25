using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tickets;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Prioridade { get; set; }
    [BindProperty(SupportsGet = true)] public string? Nivel { get; set; }
    [BindProperty(SupportsGet = true)] public string? SlaStatus { get; set; }
    [BindProperty(SupportsGet = true)] public string? Categoria { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public IEnumerable<JsonElement> Tickets { get; private set; } = Enumerable.Empty<JsonElement>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    private static readonly HashSet<string> StatusValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Aberto", "EmAtendimento", "AguardandoCliente", "Resolvido", "Fechado" };
    private static readonly HashSet<string> PrioridadesValidas = new(StringComparer.OrdinalIgnoreCase)
        { "Baixa", "Normal", "Alta", "Critica" };
    private static readonly HashSet<string> NiveisValidos = new(StringComparer.OrdinalIgnoreCase)
        { "N1", "N2", "N3", "N4" };
    private static readonly HashSet<string> SlaStatusValidos = new(StringComparer.OrdinalIgnoreCase)
        { "ok", "violado" };
    private static readonly HashSet<string> CategoriasValidas = new(StringComparer.OrdinalIgnoreCase)
        { "Bug", "Duvida", "Financeiro", "Outro", "Solicitacao", "Incidente", "BugFixDev" };

    public async Task OnGetAsync()
    {
        if (Page < 1) Page = 1;
        if (Page > 10000) Page = 10000;

        try
        {
            var qs = $"api/admin/tickets?page={Page}&pageSize=25";
            if (!string.IsNullOrWhiteSpace(Status) && StatusValidos.Contains(Status))
                qs += $"&status={Uri.EscapeDataString(Status)}";
            if (!string.IsNullOrWhiteSpace(Prioridade) && PrioridadesValidas.Contains(Prioridade))
                qs += $"&prioridade={Uri.EscapeDataString(Prioridade)}";
            if (!string.IsNullOrWhiteSpace(Nivel) && NiveisValidos.Contains(Nivel))
                qs += $"&nivel={Uri.EscapeDataString(Nivel)}";
            if (!string.IsNullOrWhiteSpace(SlaStatus) && SlaStatusValidos.Contains(SlaStatus))
                qs += $"&slaStatus={Uri.EscapeDataString(SlaStatus)}";
            if (!string.IsNullOrWhiteSpace(Categoria) && CategoriasValidas.Contains(Categoria))
                qs += $"&categoria={Uri.EscapeDataString(Categoria)}";
            if (!string.IsNullOrWhiteSpace(Search))
                qs += $"&search={Uri.EscapeDataString(Search)}";

            var raw = await api.GetRawAsync(qs);
            Tickets = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
            if (raw.TryGetProperty("meta", out var meta))
            {
                Total = meta.TryGetProperty("total", out var t) && t.TryGetInt32(out var tv) ? tv : 0;
                TotalPages = meta.TryGetProperty("pages", out var p) && p.TryGetInt32(out var pv) ? pv : 1;
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar tickets (page={Page}, status={Status})", Page, Status);
            Erro = "Não foi possível carregar a lista de tickets. Tente recarregar a página.";
        }
    }

    public async Task<IActionResult> OnPostAsync(Guid empresaId, string titulo, string descricao, string categoria, string prioridade, string? nivel)
    {
        if (empresaId == Guid.Empty)
        {
            SetErro("Selecione uma empresa válida.");
            return RedirectToPage(new { Page, Status, Prioridade, Search });
        }
        var tituloT = (titulo ?? "").Trim();
        var descricaoT = (descricao ?? "").Trim();
        var categoriaT = (categoria ?? "").Trim();
        var prioridadeT = (prioridade ?? "").Trim();
        var nivelT = string.IsNullOrWhiteSpace(nivel) ? "N1" : nivel.Trim();

        if (tituloT.Length is < 3 or > 200)
        {
            SetErro("Título deve ter entre 3 e 200 caracteres.");
            return RedirectToPage(new { Page, Status, Prioridade, Search });
        }
        if (descricaoT.Length is < 5 or > 4000)
        {
            SetErro("Descrição deve ter entre 5 e 4000 caracteres.");
            return RedirectToPage(new { Page, Status, Prioridade, Search });
        }
        if (!CategoriasValidas.Contains(categoriaT))
        {
            SetErro("Categoria inválida.");
            return RedirectToPage(new { Page, Status, Prioridade, Search });
        }
        if (!PrioridadesValidas.Contains(prioridadeT))
        {
            SetErro("Prioridade inválida.");
            return RedirectToPage(new { Page, Status, Prioridade, Search });
        }
        if (!NiveisValidos.Contains(nivelT))
        {
            SetErro("Nível inválido.");
            return RedirectToPage(new { Page, Status, Prioridade, Search });
        }

        try
        {
            await api.PostAsync<JsonElement>("api/admin/tickets",
                new { empresaId, titulo = tituloT, descricao = descricaoT, categoria = categoriaT, prioridade = prioridadeT, nivel = nivelT });
            SetSucesso("Ticket criado com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar ticket (empresaId={EmpresaId})", empresaId);
            SetErro($"Falha ao criar ticket: {ex.Message}");
        }
        return RedirectToPage(new { Page, Status, Prioridade, Search });
    }
}
