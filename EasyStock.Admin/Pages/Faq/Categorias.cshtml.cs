using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Faq;

public class CategoriasModel(AdminApiClient api, AdminSessionService session, ILogger<CategoriasModel> log) : AdminPageBase(session)
{
    [BindProperty] public string Nome { get; set; } = string.Empty;
    [BindProperty] public string Slug { get; set; } = string.Empty;
    [BindProperty] public string? Descricao { get; set; }
    [BindProperty] public string? Icone { get; set; }
    [BindProperty] public int Ordem { get; set; }

    public IEnumerable<JsonElement> Categorias { get; private set; } = Enumerable.Empty<JsonElement>();
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var raw = await api.GetRawAsync("api/admin/faq/categorias");
            Categorias = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar categorias FAQ");
            Erro = "Falha ao carregar categorias.";
        }
    }

    public async Task<IActionResult> OnPostCriarAsync()
    {
        if (string.IsNullOrWhiteSpace(Nome) || string.IsNullOrWhiteSpace(Slug))
        {
            SetErro("Nome e slug sao obrigatorios.");
            return RedirectToPage();
        }

        try
        {
            await api.PostAsync<object>("api/admin/faq/categorias", new
            {
                nome = Nome,
                slug = Slug,
                descricao = Descricao,
                icone = Icone,
                ordem = Ordem
            });
            SetSucesso("Categoria criada.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar categoria");
            SetErro("Falha ao criar categoria: " + ex.Message);
        }
        return RedirectToPage();
    }
}
