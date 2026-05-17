using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Notificacoes.Templates;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> logger)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Canal { get; set; }
    [BindProperty(SupportsGet = true)] public string? TipoEvento { get; set; }
    [BindProperty(SupportsGet = true)] public bool? Ativo { get; set; }
    [BindProperty(SupportsGet = true)] public string? Busca { get; set; }
    [BindProperty(SupportsGet = true)] public string Modo { get; set; } = "grade";

    public JsonElement Data { get; private set; }
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    public Dictionary<string, HashSet<string>> CoberturaPorEvento { get; private set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            var pageSize = Modo == "grade" ? 200 : 20;
            var qs = $"?page={Page}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(Canal)) qs += $"&canal={Canal}";
            if (!string.IsNullOrWhiteSpace(TipoEvento)) qs += $"&tipoEvento={TipoEvento}";
            if (Ativo.HasValue) qs += $"&ativo={Ativo.Value}";

            var result = await api.GetRawAsync($"api/admin/notificacoes/templates{qs}");
            Data = result.GetProperty("data");
            if (result.TryGetProperty("meta", out var meta))
            {
                Total = meta.GetProperty("total").GetInt32();
                TotalPages = meta.GetProperty("pages").GetInt32();
            }
            ConstruirCobertura();
            FiltrarPorBusca();
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao listar templates");
            Erro = "Erro ao carregar templates.";
        }
    }

    private void ConstruirCobertura()
    {
        CoberturaPorEvento = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (Data.ValueKind != JsonValueKind.Array) return;
        foreach (var t in Data.EnumerateArray())
        {
            var evento = t.TryGetProperty("tipoEvento", out var ev) ? ev.GetString() : null;
            var canal = t.TryGetProperty("canal", out var ca) ? ca.GetString() : null;
            var ativo = t.TryGetProperty("ativo", out var at) && at.GetBoolean();
            var aprovado = t.TryGetProperty("aprovado", out var ap) && ap.GetBoolean();
            if (string.IsNullOrWhiteSpace(evento) || string.IsNullOrWhiteSpace(canal)) continue;
            if (!ativo || !aprovado) continue;
            if (!CoberturaPorEvento.TryGetValue(evento, out var canais))
            {
                canais = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CoberturaPorEvento[evento] = canais;
            }
            canais.Add(canal);
        }
    }

    private void FiltrarPorBusca()
    {
        if (string.IsNullOrWhiteSpace(Busca)) return;
        if (Data.ValueKind != JsonValueKind.Array) return;
        var termo = Busca.Trim().ToLowerInvariant();
        var filtrados = new List<JsonElement>();
        foreach (var t in Data.EnumerateArray())
        {
            var codigo = (t.TryGetProperty("codigo", out var c) ? c.GetString() : "") ?? "";
            var nome = (t.TryGetProperty("nome", out var n) ? n.GetString() : "") ?? "";
            var evento = (t.TryGetProperty("tipoEvento", out var ev) ? ev.GetString() : "") ?? "";
            if (codigo.ToLowerInvariant().Contains(termo)
                || nome.ToLowerInvariant().Contains(termo)
                || evento.ToLowerInvariant().Contains(termo))
                filtrados.Add(t);
        }
        var json = "[" + string.Join(",", filtrados.Select(x => x.GetRawText())) + "]";
        Data = JsonDocument.Parse(json).RootElement.Clone();
    }

    public async Task<IActionResult> OnPostAprovarAsync(Guid id)
    {
        try
        {
            await api.PostRawAsync($"api/admin/notificacoes/templates/{id}/aprovar", new { });
            SetSucesso("Template aprovado com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex) { SetErro(ex.Message); }
        return RedirectToPage();
    }
}
