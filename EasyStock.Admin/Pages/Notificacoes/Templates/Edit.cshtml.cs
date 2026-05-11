using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Notificacoes.Templates;

public class EditModel(AdminApiClient api, AdminSessionService session, ILogger<EditModel> logger)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }

    [BindProperty] public string Codigo { get; set; } = "";
    [BindProperty] public string Nome { get; set; } = "";
    [BindProperty] public string Canal { get; set; } = "Email";
    [BindProperty] public string TipoEvento { get; set; } = "AlertaEstoqueCritico";
    [BindProperty] public string AssuntoTemplate { get; set; } = "";
    [BindProperty] public string CorpoTemplate { get; set; } = "";
    [BindProperty] public string Idioma { get; set; } = "pt-BR";

    public JsonElement? TemplateAtual { get; private set; }
    public List<VariavelOpcao> VariaveisDisponiveis { get; private set; } = new();
    public Dictionary<string, object?> InitialVars { get; private set; } = new();
    public int Versao { get; private set; }
    public bool Aprovado { get; private set; }
    public bool Ativo { get; private set; }
    public string? Erro { get; private set; }

    public sealed record VariavelOpcao(string Nome, string Tipo, string Descricao, string Exemplo);

    public async Task OnGetAsync()
    {
        if (Id.HasValue)
        {
            try
            {
                var result = await api.GetRawAsync($"api/admin/notificacoes/templates/{Id}");
                TemplateAtual = result.GetProperty("data");
                var t = TemplateAtual.Value;
                Codigo = t.GetProperty("codigo").GetString()!;
                Nome = t.GetProperty("nome").GetString()!;
                Canal = t.GetProperty("canal").GetString()!;
                TipoEvento = t.GetProperty("tipoEvento").GetString()!;
                AssuntoTemplate = t.GetProperty("assuntoTemplate").GetString()!;
                CorpoTemplate = t.GetProperty("corpoTemplate").GetString()!;
                Idioma = t.TryGetProperty("idioma", out var id) ? id.GetString() ?? "pt-BR" : "pt-BR";
                Versao = t.TryGetProperty("versao", out var v) ? v.GetInt32() : 0;
                Aprovado = t.TryGetProperty("aprovado", out var ap) && ap.GetBoolean();
                Ativo = t.TryGetProperty("ativo", out var at) && at.GetBoolean();
            }
            catch (SessionExpiredException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao carregar template {Id}", Id);
                Erro = "Erro ao carregar template.";
            }
        }
        await CarregarVariaveisAsync();
        PopularInitialVars();
    }

    private async Task CarregarVariaveisAsync()
    {
        if (string.IsNullOrWhiteSpace(TipoEvento)) return;
        try
        {
            var result = await api.GetRawAsync($"api/admin/notificacoes/variaveis-catalogo?tipoEvento={TipoEvento}");
            var data = result.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Array) return;
            foreach (var v in data.EnumerateArray())
            {
                VariaveisDisponiveis.Add(new VariavelOpcao(
                    v.GetProperty("nomeVariavel").GetString() ?? "",
                    v.TryGetProperty("tipo", out var tp) ? tp.GetString() ?? "" : "",
                    v.TryGetProperty("descricao", out var ds) ? ds.GetString() ?? "" : "",
                    v.TryGetProperty("exemplo", out var ex) ? ex.GetString() ?? "" : ""));
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao carregar variáveis disponíveis para TipoEvento {TipoEvento}", TipoEvento);
        }
    }

    private void PopularInitialVars()
    {
        foreach (var v in VariaveisDisponiveis)
        {
            object? valor = v.Tipo.ToLowerInvariant() switch
            {
                "int" or "integer" or "number" => int.TryParse(v.Exemplo, out var n) ? n : 0,
                "bool" or "boolean" => bool.TryParse(v.Exemplo, out var b) && b,
                _ => string.IsNullOrEmpty(v.Exemplo) ? v.Nome : v.Exemplo
            };
            InitialVars[v.Nome] = valor;
        }

        // Fallback defaults para variáveis comuns (caso não catalogadas).
        if (!InitialVars.ContainsKey("nomeProduto")) InitialVars["nomeProduto"] = "Produto Exemplo";
        if (!InitialVars.ContainsKey("nomeUsuario")) InitialVars["nomeUsuario"] = "Usuário Exemplo";
        if (!InitialVars.ContainsKey("email")) InitialVars["email"] = "usuario@exemplo.com";
        if (!InitialVars.ContainsKey("diasRestantes")) InitialVars["diasRestantes"] = 3;
        if (!InitialVars.ContainsKey("__unsubscribe_url")) InitialVars["__unsubscribe_url"] = "#";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (Id.HasValue)
            {
                await api.PutRawAsync($"api/admin/notificacoes/templates/{Id}", new
                {
                    novoAssunto = AssuntoTemplate,
                    novoCorpo = CorpoTemplate
                });
                SetSucesso("Template atualizado. Nova versão criada.");
            }
            else
            {
                await api.PostRawAsync("api/admin/notificacoes/templates", new
                {
                    codigo = Codigo, nome = Nome, canal = Canal,
                    tipoEvento = TipoEvento, assuntoTemplate = AssuntoTemplate,
                    corpoTemplate = CorpoTemplate, idioma = Idioma
                });
                SetSucesso("Template criado com sucesso.");
            }
            return RedirectToPage("Index");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            SetErro(ex.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAprovarAsync(string? motivo = null)
    {
        if (!Id.HasValue)
        {
            SetErro("Salve antes de aprovar.");
            return Page();
        }
        try
        {
            if (!string.IsNullOrWhiteSpace(motivo))
                logger.LogInformation("Aprovando template {Id}. Motivo: {Motivo}", Id, motivo);
            await api.PostRawAsync($"api/admin/notificacoes/templates/{Id}/aprovar", new { });
            SetSucesso("Template aprovado com sucesso.");
            return RedirectToPage("Index");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            SetErro(ex.Message);
            return Page();
        }
    }
}
