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
    public string? Erro { get; private set; }

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
                    codigo = Codigo, nome = Nome,
                    tipoEvento = TipoEvento, triggerTipo = TriggerTipo,
                    templateCodigo = TemplateCodigo, categoria = Categoria,
                    cronExpression = CronExpression, parametrosJson = ParametrosJson
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
