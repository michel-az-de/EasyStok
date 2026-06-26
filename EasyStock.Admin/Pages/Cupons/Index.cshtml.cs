namespace EasyStock.Admin.Pages.Cupons;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    public IEnumerable<JsonElement> Cupons { get; private set; } = Enumerable.Empty<JsonElement>();
    public IEnumerable<JsonElement> Planos { get; private set; } = Enumerable.Empty<JsonElement>();

    private static readonly HashSet<string> TiposValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Percentual", "ValorFixo", "MesesGratis" };

    public async Task OnGetAsync()
    {
        try
        {
            Cupons = await api.GetAsync<IEnumerable<JsonElement>>("api/admin/cupons");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar cupons");
            SetErro("Não foi possível carregar a lista de cupons.");
        }

        try
        {
            var planosRaw = await api.GetAsync<JsonElement>("api/admin/planos");
            Planos = planosRaw.ValueKind == JsonValueKind.Array
                ? planosRaw.EnumerateArray().ToList()
                : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex) { log.LogWarning(ex, "Falha ao carregar planos para o seletor de cupons"); }
    }

    public async Task<IActionResult> OnPostCriarAsync(
        string codigo, string tipoDesconto, decimal valor,
        int? limiteUsos, string? validoAte, string? planoId)
    {
        if (!TryValidateCupom(codigo, tipoDesconto, valor, limiteUsos, validoAte, planoId,
                out var codigoT, out var tipoT, out var validoAteDt, out var planoIdGuid, out var erro))
        {
            SetErro(erro);
            return RedirectToPage();
        }

        try
        {
            await api.PostAsync<JsonElement>("api/admin/cupons",
                new { codigo = codigoT, tipoDesconto = tipoT, valor, limiteUsos, validoAte = validoAteDt, planoId = planoIdGuid });
            SetSucesso($"Cupom \"{codigoT}\" criado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar cupom {Codigo}", codigoT);
            SetErroSeguro(ex, "Criar cupom");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditarAsync(
        Guid id, string codigo, string tipoDesconto, decimal valor,
        int? limiteUsos, string? validoAte, string? planoId)
    {
        if (id == Guid.Empty)
        {
            SetErro("Cupom inválido.");
            return RedirectToPage();
        }
        if (!TryValidateCupom(codigo, tipoDesconto, valor, limiteUsos, validoAte, planoId,
                out var codigoT, out var tipoT, out var validoAteDt, out var planoIdGuid, out var erro))
        {
            SetErro(erro);
            return RedirectToPage();
        }

        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/cupons/{id}",
                new { codigo = codigoT, tipoDesconto = tipoT, valor, limiteUsos, validoAte = validoAteDt, planoId = planoIdGuid });
            SetSucesso("Cupom atualizado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao editar cupom {CupomId}", id);
            SetErroSeguro(ex, "Editar cupom");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, string? codigo, bool atualmenteAtivo)
    {
        if (id == Guid.Empty)
        {
            SetErro("Cupom inválido.");
            return RedirectToPage();
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/cupons/{id}/toggle", new { });
            var label = codigo is { Length: > 0 } ? $"\"{codigo}\"" : "Cupom";
            SetSucesso(atualmenteAtivo
                ? $"{label} desativado. Não será aceito em novas assinaturas."
                : $"{label} ativado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alternar cupom {CupomId}", id);
            SetErroSeguro(ex, "Alterar status do cupom");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeletarAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            SetErro("Cupom inválido.");
            return RedirectToPage();
        }
        try
        {
            await api.DeleteAsync($"api/admin/cupons/{id}");
            SetSucesso("Cupom removido.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao deletar cupom {CupomId}", id);
            SetErroSeguro(ex, "Remover cupom");
        }
        return RedirectToPage();
    }

    private static bool TryValidateCupom(
        string? codigo, string? tipoDesconto, decimal valor, int? limiteUsos,
        string? validoAte, string? planoId,
        out string codigoT, out string tipoT, out DateTime? validoAteDt, out Guid? planoIdGuid,
        out string erro)
    {
        codigoT = (codigo ?? "").Trim();
        tipoT = (tipoDesconto ?? "").Trim();
        validoAteDt = null;
        planoIdGuid = null;
        erro = "";

        if (codigoT.Length is < 3 or > 50)
        {
            erro = "Código do cupom deve ter entre 3 e 50 caracteres.";
            return false;
        }
        foreach (var ch in codigoT)
        {
            if (ch is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
            {
                erro = "Código do cupom só pode conter letras, números, hífen e underscore.";
                return false;
            }
        }
        if (!TiposValidos.Contains(tipoT))
        {
            erro = "Tipo de desconto deve ser \"Percentual\", \"ValorFixo\" ou \"MesesGratis\".";
            return false;
        }
        if (valor <= 0)
        {
            erro = "Valor do desconto deve ser maior que zero.";
            return false;
        }
        if (string.Equals(tipoT, "Percentual", StringComparison.OrdinalIgnoreCase) && valor > 100)
        {
            erro = "Desconto percentual não pode passar de 100%.";
            return false;
        }
        // Teto = precisão da coluna Cupom.Valor decimal(10,2) no backend. Valida aqui pra
        // mostrar mensagem clara (canal SetErro) em vez da falha silenciosa do INSERT (#693).
        if (valor > 99_999_999.99m)
        {
            erro = "Valor do desconto é muito alto. Máximo permitido: 99.999.999,99.";
            return false;
        }
        if (limiteUsos is < 1)
        {
            erro = "Limite de usos deve ser maior que zero.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(validoAte))
        {
            if (!DateTime.TryParse(validoAte, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out var dt)
                && !DateTime.TryParse(validoAte, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                erro = "Data de validade inválida.";
                return false;
            }
            if (dt < DateTime.UtcNow.Date)
            {
                erro = "Data de validade não pode estar no passado.";
                return false;
            }
            validoAteDt = dt;
        }
        if (!string.IsNullOrWhiteSpace(planoId))
        {
            if (!Guid.TryParse(planoId, out var pg))
            {
                erro = "Plano selecionado é inválido.";
                return false;
            }
            planoIdGuid = pg;
        }

        return true;
    }
}
