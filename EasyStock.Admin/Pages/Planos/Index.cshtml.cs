using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Planos;

public class IndexModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    public IEnumerable<JsonElement> Planos { get; private set; } = Enumerable.Empty<JsonElement>();
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var data = await api.GetAsync<JsonElement>("api/admin/planos");
            Planos = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex) { Erro = ex.Message; }
    }

    public async Task<IActionResult> OnPostCriarAsync(
        string nome, string? descricao, int limiteLojas, int limiteUsuarios,
        int limiteProdutos, int limiteGeracoesIaMensais, decimal precoMensal)
    {
        await api.PostAsync<JsonElement>("api/admin/planos",
            new { nome, descricao, limiteLojas, limiteUsuarios, limiteProdutos, limiteGeracoesIaMensais, precoMensal });
        SetSucesso($"Plano \"{nome}\" criado com sucesso.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditarAsync(
        Guid id, string nome, string? descricao, int limiteLojas, int limiteUsuarios,
        int limiteProdutos, int limiteGeracoesIaMensais, decimal precoMensal)
    {
        await api.PatchAsync<JsonElement>($"api/admin/planos/{id}",
            new { nome, descricao, limiteLojas, limiteUsuarios, limiteProdutos, limiteGeracoesIaMensais, precoMensal });
        SetSucesso("Plano atualizado com sucesso.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        await api.PatchAsync<JsonElement>($"api/admin/planos/{id}/toggle", new { });
        SetSucesso("Status do plano alterado.");
        return RedirectToPage();
    }
}
