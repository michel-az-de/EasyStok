using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Notificacoes.Templates;

public class EditModel(AdminApiClient api, AdminSessionService session, ILogger<EditModel> logger)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? DuplicarDe { get; set; }
    [BindProperty(SupportsGet = true)] public string? CanalAlvo { get; set; }

    [BindProperty] public string Codigo { get; set; } = "";
    [BindProperty] public string Nome { get; set; } = "";
    [BindProperty] public string Canal { get; set; } = "Email";
    [BindProperty] public string TipoEvento { get; set; } = "AlertaEstoqueCritico";
    [BindProperty] public string AssuntoTemplate { get; set; } = "";
    [BindProperty] public string CorpoTemplate { get; set; } = "";
    [BindProperty] public string Idioma { get; set; } = "pt-BR";

    public JsonElement? TemplateAtual { get; private set; }
    public string? Erro { get; private set; }
    public int Versao { get; private set; } = 1;
    public bool Aprovado { get; private set; }
    public bool Ativo { get; private set; }
    public DateTime? AtualizadoEm { get; private set; }
    public string? AtualizadoPor { get; private set; }

    public sealed record VariavelOpcao(string Nome, string Tipo, string Descricao, string Exemplo);

    public async Task OnGetAsync()
    {
        if (Id.HasValue) { await CarregarTemplateAsync(Id.Value); return; }
        if (DuplicarDe.HasValue) await CarregarDuplicandoAsync(DuplicarDe.Value);
    }

    private async Task CarregarTemplateAsync(Guid id)
    {
        try
        {
            var result = await api.GetRawAsync($"api/admin/notificacoes/templates/{id}");
            TemplateAtual = result.GetProperty("data");
            var t = TemplateAtual.Value;
            Codigo = t.GetProperty("codigo").GetString()!;
            Nome = t.GetProperty("nome").GetString()!;
            Canal = t.GetProperty("canal").GetString()!;
            TipoEvento = t.GetProperty("tipoEvento").GetString()!;
            AssuntoTemplate = t.GetProperty("assuntoTemplate").GetString()!;
            CorpoTemplate = t.GetProperty("corpoTemplate").GetString()!;
            Idioma = t.TryGetProperty("idioma", out var id1) ? id1.GetString() ?? "pt-BR" : "pt-BR";
            Versao = t.TryGetProperty("versao", out var ve) ? ve.GetInt32() : 1;
            Aprovado = t.TryGetProperty("aprovado", out var ap) && ap.GetBoolean();
            Ativo = t.TryGetProperty("ativo", out var at) && at.GetBoolean();
            if (t.TryGetProperty("atualizadoEm", out var ae) && ae.ValueKind == JsonValueKind.String)
                AtualizadoEm = ae.GetDateTime();
            AtualizadoPor = t.TryGetProperty("atualizadoPor", out var ap2) ? ap2.GetString() : null;
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao carregar template {Id}", id);
            Erro = "Erro ao carregar template.";
        }
    }

    private async Task CarregarDuplicandoAsync(Guid origemId)
    {
        try
        {
            var result = await api.GetRawAsync($"api/admin/notificacoes/templates/{origemId}");
            var t = result.GetProperty("data");
            var canalDestino = CanalAlvo ?? t.GetProperty("canal").GetString() ?? "Email";
            Codigo = SugerirCodigoDuplicado(t.GetProperty("codigo").GetString()!, canalDestino);
            Nome = $"{t.GetProperty("nome").GetString()} (cópia)";
            Canal = canalDestino;
            TipoEvento = t.GetProperty("tipoEvento").GetString()!;
            AssuntoTemplate = t.GetProperty("assuntoTemplate").GetString()!;
            CorpoTemplate = t.GetProperty("corpoTemplate").GetString()!;
            Idioma = t.TryGetProperty("idioma", out var id1) ? id1.GetString() ?? "pt-BR" : "pt-BR";
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao carregar template origem {OrigemId}", origemId);
            Erro = "Erro ao carregar template de origem.";
        }
    }

    private static string SugerirCodigoDuplicado(string codigoOriginal, string canalDestino)
    {
        var canalLower = canalDestino.ToLowerInvariant();
        var sufixos = new[] { "_email_v1", "_inapp_v1", "_sms_v1", "_whatsapp_v1" };
        foreach (var s in sufixos)
            if (codigoOriginal.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                return $"{codigoOriginal[..^s.Length]}_{canalLower}_v1";
        return $"{codigoOriginal}_{canalLower}_v1";
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
                SetSucesso("Template atualizado. Nova versao criada — aprove para ativar.");
            }
            else
            {
                await api.PostRawAsync("api/admin/notificacoes/templates", new
                {
                    codigo = Codigo, nome = Nome, canal = Canal,
                    tipoEvento = TipoEvento, assuntoTemplate = AssuntoTemplate,
                    corpoTemplate = CorpoTemplate, idioma = Idioma
                });
                SetSucesso("Template criado. Aprove para ativar.");
            }
            return RedirectToPage("Index");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            SetErroSeguro(ex, "Salvar template");
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAprovarAsync()
    {
        if (!Id.HasValue) { SetErro("Salve o template antes de aprovar."); return RedirectToPage(); }
        try
        {
            await api.PostRawAsync($"api/admin/notificacoes/templates/{Id}/aprovar", new { });
            SetSucesso("Template aprovado e ativo.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex) { SetErroSeguro(ex, "Aprovar template"); }
        return RedirectToPage("Index");
    }
}
