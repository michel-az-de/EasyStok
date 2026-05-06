using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Cupons;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    public IEnumerable<JsonElement> Cupons { get; private set; } = Enumerable.Empty<JsonElement>();
    public IEnumerable<JsonElement> Planos { get; private set; } = Enumerable.Empty<JsonElement>();
    public string? Erro { get; private set; }
    public string? Mensagem { get; private set; }

    private static readonly HashSet<string> TiposValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Percentual", "ValorFixo" };

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
            Erro = "Não foi possível carregar a lista de cupons.";
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
            SetErro($"Falha ao criar cupom: {ex.Message}");
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
            SetErro($"Falha ao editar cupom: {ex.Message}");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            SetErro("Cupom inválido.");
            return RedirectToPage();
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/cupons/{id}/toggle", new { });
            SetSucesso("Status do cupom alterado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alternar cupom {CupomId}", id);
            SetErro($"Falha ao alterar status: {ex.Message}");
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
            SetErro($"Falha ao remover cupom: {ex.Message}");
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
        if (!TiposValidos.Contains(tipoT))
        {
            erro = "Tipo de desconto deve ser \"Percentual\" ou \"ValorFixo\".";
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
