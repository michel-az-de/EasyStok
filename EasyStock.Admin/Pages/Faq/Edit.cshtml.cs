using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Faq;

public class EditModel(AdminApiClient api, AdminSessionService session, ILogger<EditModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }

    [BindProperty] public Guid CategoriaId { get; set; }
    [BindProperty] public string Titulo { get; set; } = string.Empty;
    [BindProperty] public string Slug { get; set; } = string.Empty;
    [BindProperty] public string Conteudo { get; set; } = string.Empty;
    [BindProperty] public string? ConteudoBusca { get; set; }
    [BindProperty] public string? Tags { get; set; }
    [BindProperty] public int Ordem { get; set; }

    public IEnumerable<JsonElement> Categorias { get; private set; } = Enumerable.Empty<JsonElement>();
    public string? Status { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        await CarregarCategorias();

        if (Id.HasValue && Id.Value != Guid.Empty)
        {
            try
            {
                // reaproveita listagem com filtro pelo proprio id (poderia ter endpoint dedicado, mas mantemos minimo)
                var raw = await api.GetRawAsync($"api/admin/faq/itens?page=1&pageSize=200");
                if (raw.TryGetProperty("data", out var data) && data.TryGetProperty("itens", out var itens))
                {
                    foreach (var it in itens.EnumerateArray())
                    {
                        if (it.GetProperty("id").GetGuid() == Id.Value)
                        {
                            CategoriaId = it.GetProperty("categoriaId").GetGuid();
                            Titulo = it.GetProperty("titulo").GetString() ?? "";
                            Slug = it.GetProperty("slug").GetString() ?? "";
                            Status = it.GetProperty("status").GetString();
                            ConteudoBusca = it.TryGetProperty("resumo", out var r) ? r.GetString() : null;
                            if (it.TryGetProperty("tags", out var tagsArr) && tagsArr.ValueKind == JsonValueKind.Array)
                                Tags = string.Join(", ", tagsArr.EnumerateArray().Select(t => t.GetString()));
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha ao carregar item FAQ {Id}", Id);
                Erro = "Falha ao carregar item.";
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Titulo) || string.IsNullOrWhiteSpace(Conteudo))
        {
            await CarregarCategorias();
            Erro = "Titulo e conteudo sao obrigatorios.";
            return Page();
        }

        var tags = string.IsNullOrWhiteSpace(Tags)
            ? Array.Empty<string>()
            : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            if (Id.HasValue && Id.Value != Guid.Empty)
            {
                await api.PutAsync($"api/admin/faq/itens/{Id.Value}", new
                {
                    titulo = Titulo,
                    conteudo = Conteudo,
                    conteudoBusca = ConteudoBusca,
                    tags,
                    ordem = Ordem
                });
                SetSucesso("Item atualizado.");
            }
            else
            {
                if (CategoriaId == Guid.Empty || string.IsNullOrWhiteSpace(Slug))
                {
                    await CarregarCategorias();
                    Erro = "Categoria e slug sao obrigatorios.";
                    return Page();
                }

                var resp = await api.PostJsonAsync<JsonElement>("api/admin/faq/itens", new
                {
                    categoriaId = CategoriaId,
                    titulo = Titulo,
                    slug = Slug,
                    conteudo = Conteudo,
                    conteudoBusca = ConteudoBusca,
                    tags,
                    ordem = Ordem
                });
                SetSucesso("Item criado.");
            }
            return RedirectToPage("/Faq/Index");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao salvar FAQ");
            await CarregarCategorias();
            Erro = "Falha ao salvar: " + ex.Message;
            return Page();
        }
    }

    private async Task CarregarCategorias()
    {
        try
        {
            var raw = await api.GetRawAsync("api/admin/faq/categorias");
            Categorias = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar categorias");
        }
    }
}
