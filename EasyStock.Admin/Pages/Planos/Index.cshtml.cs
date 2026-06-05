namespace EasyStock.Admin.Pages.Planos;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
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
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar planos");
            Erro = ex.Message;
        }
    }

    public async Task<IActionResult> OnPostCriarAsync(
        string nome, string? descricao, int limiteLojas, int limiteUsuarios,
        int limiteProdutos, int limiteGeracoesIaMensais, decimal precoMensal)
    {
        if (!TryValidatePlano(nome, limiteLojas, limiteUsuarios, limiteProdutos,
                limiteGeracoesIaMensais, precoMensal, out var nomeT, out var erro))
        {
            SetErro(erro);
            return RedirectToPage();
        }

        try
        {
            await api.PostAsync<JsonElement>("api/admin/planos",
                new { nome = nomeT, descricao, limiteLojas, limiteUsuarios, limiteProdutos, limiteGeracoesIaMensais, precoMensal });
            SetSucesso($"Plano \"{nomeT}\" criado com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar plano {Nome}", nomeT);
            SetErroSeguro(ex, "Criar plano");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditarAsync(
        Guid id, string nome, string? descricao, int limiteLojas, int limiteUsuarios,
        int limiteProdutos, int limiteGeracoesIaMensais, decimal precoMensal)
    {
        if (id == Guid.Empty)
        {
            SetErro("Plano inválido.");
            return RedirectToPage();
        }
        if (!TryValidatePlano(nome, limiteLojas, limiteUsuarios, limiteProdutos,
                limiteGeracoesIaMensais, precoMensal, out var nomeT, out var erro))
        {
            SetErro(erro);
            return RedirectToPage();
        }

        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/planos/{id}",
                new { nome = nomeT, descricao, limiteLojas, limiteUsuarios, limiteProdutos, limiteGeracoesIaMensais, precoMensal });
            SetSucesso("Plano atualizado com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao editar plano {PlanoId}", id);
            SetErroSeguro(ex, "Editar plano");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        await api.PatchAsync<JsonElement>($"api/admin/planos/{id}/toggle", new { });
        SetSucesso("Status do plano alterado.");
        return RedirectToPage();
    }

    /// <summary>
    /// Validacao server-side do plano antes de chamar a API. Espelha PlanoValidacao na
    /// fronteira da API e o padrao de TryValidateCupom: limite -1 = ilimitado; valores
    /// menores que -1 sao rejeitados; preco deve ser >= 0. Fecha BUG-002/003 (Plano
    /// aceitava negativos enquanto Cupom ja validava).
    /// </summary>
    private static bool TryValidatePlano(
        string? nome, int limiteLojas, int limiteUsuarios, int limiteProdutos,
        int limiteGeracoesIaMensais, decimal precoMensal,
        out string nomeT, out string erro)
    {
        const int semLimite = -1;
        nomeT = (nome ?? "").Trim();
        erro = "";

        if (nomeT.Length is < 2 or > 80)
        {
            erro = "Nome do plano deve ter entre 2 e 80 caracteres.";
            return false;
        }

        (int valor, string campo)[] limites =
        {
            (limiteLojas, "Limite de lojas"),
            (limiteUsuarios, "Limite de usuários"),
            (limiteProdutos, "Limite de produtos"),
            (limiteGeracoesIaMensais, "Limite de IA/mês"),
        };
        foreach (var (valor, campo) in limites)
        {
            if (valor < semLimite)
            {
                erro = $"{campo} deve ser -1 (ilimitado) ou um valor não-negativo.";
                return false;
            }
        }

        if (precoMensal < 0)
        {
            erro = "Preço mensal não pode ser negativo.";
            return false;
        }

        return true;
    }
}
