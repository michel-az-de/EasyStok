using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Notificacoes.Rotinas;

public class EditModel(AdminApiClient api, AdminSessionService session, ILogger<EditModel> logger)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }

    [BindProperty] public string Codigo { get; set; } = "";
    [BindProperty] public string Nome { get; set; } = "";
    [BindProperty] public string TipoEvento { get; set; } = "AlertaEstoqueCritico";
    [BindProperty] public string TriggerTipo { get; set; } = "Cron";
    [BindProperty] public string TemplateCodigo { get; set; } = "";
    [BindProperty] public string Categoria { get; set; } = "Operacional";
    [BindProperty] public string? CronExpression { get; set; }
    [BindProperty] public string? ParametrosJson { get; set; }

    public JsonElement? RotinaAtual { get; private set; }
    public List<TemplateOpcao> TemplatesDisponiveis { get; private set; } = new();
    public string? Erro { get; private set; }

    public sealed record TemplateOpcao(string Codigo, string Nome, string Canal, string TipoEvento);

    public async Task OnGetAsync()
    {
        if (Id.HasValue)
        {
            try
            {
                var result = await api.GetRawAsync($"api/admin/notificacoes/rotinas/{Id}");
                RotinaAtual = result.GetProperty("data");
                var r = RotinaAtual.Value;
                Codigo = r.GetProperty("codigo").GetString()!;
                Nome = r.GetProperty("nome").GetString()!;
                TipoEvento = r.GetProperty("tipoEvento").GetString()!;
                TriggerTipo = r.GetProperty("triggerTipo").GetString()!;
                TemplateCodigo = r.GetProperty("templateCodigo").GetString()!;
                Categoria = r.TryGetProperty("categoriaConteudo", out var cat) ? cat.GetString()! : "Operacional";
                CronExpression = r.TryGetProperty("cronExpression", out var ce) ? ce.GetString() : null;
                ParametrosJson = r.TryGetProperty("parametrosJson", out var pj) ? pj.GetString() : null;
            }
            catch (SessionExpiredException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao carregar rotina {Id}", Id);
                Erro = "Erro ao carregar rotina.";
            }
        }
        else
        {
            await CarregarTemplatesDisponiveisAsync();
        }
    }

    private async Task CarregarTemplatesDisponiveisAsync()
    {
        try
        {
            var result = await api.GetRawAsync("api/admin/notificacoes/templates?pageSize=200");
            var data = result.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Array) return;

            foreach (var t in data.EnumerateArray())
            {
                TemplatesDisponiveis.Add(new TemplateOpcao(
                    t.GetProperty("codigo").GetString() ?? "",
                    t.GetProperty("nome").GetString() ?? "",
                    t.GetProperty("canal").GetString() ?? "",
                    t.GetProperty("tipoEvento").GetString() ?? ""));
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao carregar templates disponíveis para o dropdown");
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (Id.HasValue)
            {
                await api.PatchRawAsync($"api/admin/notificacoes/rotinas/{Id}", new
                {
                    cronExpression = CronExpression,
                    parametrosJson = ParametrosJson,
                    atualizadoPor = "admin"
                });
                SetSucesso("Rotina atualizada.");
            }
            else
            {
                await api.PostRawAsync("api/admin/notificacoes/rotinas", new
                {
                    codigo = Codigo,
                    nome = Nome,
                    tipoEvento = TipoEvento,
                    triggerTipo = TriggerTipo,
                    templateCodigo = TemplateCodigo,
                    categoria = Categoria,
                    cronExpression = CronExpression,
                    parametrosJson = ParametrosJson
                });
                SetSucesso("Rotina criada com sucesso.");
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
}
