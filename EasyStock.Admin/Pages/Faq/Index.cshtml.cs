using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Faq;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CategoriaId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Busca { get; set; }

    public IEnumerable<JsonElement> Itens { get; private set; } = Enumerable.Empty<JsonElement>();
    public IEnumerable<JsonElement> Categorias { get; private set; } = Enumerable.Empty<JsonElement>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    private static readonly HashSet<string> StatusValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Rascunho", "Publicado", "Arquivado" };

    public async Task OnGetAsync()
    {
        if (Page < 1) Page = 1;

        try
        {
            // categorias para filtro/dropdown
            var rawCats = await api.GetRawAsync("api/admin/faq/categorias");
            Categorias = rawCats.TryGetProperty("data", out var d1) ? d1.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();

            // itens
            var qs = $"api/admin/faq/itens?page={Page}&pageSize=20";
            if (!string.IsNullOrWhiteSpace(Status) && StatusValidos.Contains(Status))
                qs += $"&status={Uri.EscapeDataString(Status)}";
            if (CategoriaId.HasValue && CategoriaId.Value != Guid.Empty)
                qs += $"&categoriaId={CategoriaId.Value}";
            if (!string.IsNullOrWhiteSpace(Busca))
                qs += $"&busca={Uri.EscapeDataString(Busca.Trim())}";

            var raw = await api.GetRawAsync(qs);
            if (raw.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("itens", out var itens))
                    Itens = itens.EnumerateArray().ToList();
                if (data.TryGetProperty("total", out var t) && t.TryGetInt32(out var tv)) Total = tv;
                if (data.TryGetProperty("pageSize", out var ps) && ps.TryGetInt32(out var psv) && psv > 0)
                    TotalPages = (Total + psv - 1) / psv;
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar FAQ admin");
            Erro = "Nao foi possivel carregar FAQ.";
        }
    }

    public async Task<IActionResult> OnPostPublicarAsync(Guid id)
    {
        try
        {
            await api.PostAsync<object>($"api/admin/faq/itens/{id}/publicar", new { });
            SetSucesso("Item publicado.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao publicar FAQ {Id}", id);
            SetErro("Falha ao publicar.");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostArquivarAsync(Guid id)
    {
        try
        {
            await api.PostAsync<object>($"api/admin/faq/itens/{id}/arquivar", new { });
            SetSucesso("Item arquivado.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao arquivar FAQ {Id}", id);
            SetErro("Falha ao arquivar.");
        }
        return RedirectToPage();
    }
}
