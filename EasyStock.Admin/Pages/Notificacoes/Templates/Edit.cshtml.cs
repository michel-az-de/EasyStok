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
    public JsonElement? PreviewResult { get; private set; }
    public List<VariavelOpcao> VariaveisDisponiveis { get; private set; } = new();
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
            }
            catch (SessionExpiredException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao carregar template {Id}", Id);
                Erro = "Erro ao carregar template.";
            }
        }
        await CarregarVariaveisAsync();
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

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (!Id.HasValue)
        {
            SetErro("Salve o template antes de visualizar o preview.");
            return Page();
        }
        try
        {
            var result = await api.PostRawAsync("api/admin/notificacoes/templates/preview", new
            {
                templateId = Id.Value,
                variaveis = new Dictionary<string, object?>
                {
                    // ProdutoVencendo / AlertaEstoque
                    ["nomeProduto"] = "Produto Exemplo",
                    ["diasRestantes"] = 3,
                    ["expiraEm"] = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
                    ["quantidade"] = 5,
                    // AssinaturaExpirando
                    ["dataExpiracao"] = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
                    ["eTrial"] = false,
                    // ResetSenha / ConfirmacaoEmail
                    ["nomeUsuario"] = "Usuário Exemplo",
                    ["email"] = "usuario@exemplo.com",
                    ["linkRedefinicao"] = "#",
                    ["linkConfirmacao"] = "#",
                    // Footer LGPD
                    ["__unsubscribe_url"] = "#"
                }
            });
            PreviewResult = result.GetProperty("data");
            await OnGetAsync();
            return Page();
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            SetErro(ex.Message);
            return Page();
        }
    }
}
