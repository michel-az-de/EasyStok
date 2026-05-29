namespace EasyStock.Admin.Pages.Configuracoes;

public class SlaMatrizModel(AdminApiClient api, AdminSessionService session, ILogger<SlaMatrizModel> log) : AdminPageBase(session)
{
    public IReadOnlyList<JsonElement> Itens { get; private set; } = Array.Empty<JsonElement>();
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var raw = await api.GetRawAsync("api/admin/sla");
            Itens = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : new List<JsonElement>();
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar matriz SLA");
            Erro = "Não foi possível carregar a matriz SLA.";
        }
    }

    public async Task<IActionResult> OnPostAsync(SalvarMatrizForm form)
    {
        try
        {
            var itens = new List<object>();
            for (int i = 0; i < (form.Prioridades?.Count ?? 0); i++)
            {
                if (form.MinutosResposta is null || form.MinutosResolucao is null) break;
                if (i >= form.MinutosResposta.Count || i >= form.MinutosResolucao.Count) break;
                if (form.MinutosResposta[i] <= 0 || form.MinutosResolucao[i] <= 0) continue;

                itens.Add(new
                {
                    empresaId = (Guid?)null,
                    planoId = (Guid?)null,
                    prioridade = form.Prioridades![i],
                    minutosResposta = form.MinutosResposta[i],
                    minutosResolucao = form.MinutosResolucao[i]
                });
            }

            await api.PutRawAsync("api/admin/sla", new { itens });
            SetSucesso("Matriz SLA salva.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao salvar matriz SLA");
            SetErro($"Falha ao salvar: {ex.Message}");
        }
        return RedirectToPage();
    }

    public sealed class SalvarMatrizForm
    {
        public List<string>? Prioridades { get; set; }
        public List<int>? MinutosResposta { get; set; }
        public List<int>? MinutosResolucao { get; set; }
    }
}
