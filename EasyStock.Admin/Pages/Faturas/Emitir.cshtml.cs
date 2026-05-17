using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Faturas;

/// <summary>
/// Emissão de fatura avulsa pelo admin. Aceita arrays paralelos de itens
/// montados pelo form Alpine (itemDescricao[], itemQuantidade[], etc.).
/// </summary>
public class EmitirModel(AdminApiClient api, AdminSessionService session, ILogger<EmitirModel> log) : AdminPageBase(session)
{
    public string? Erro { get; private set; }

    private static readonly HashSet<string> TiposItemValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Produto", "Servico", "Recorrencia", "Desconto", "Taxa" };

    public void OnGet() { }

    /// <summary>
    /// Aceita arrays paralelos para itens (dinamicos no form via Alpine).
    /// </summary>
    public async Task<IActionResult> OnPostAsync(
        Guid empresaId,
        string faturadoNome,
        string? faturadoDocumento,
        string? faturadoEmail,
        string? faturadoTelefone,
        DateTime dataVencimento,
        string[]? itemDescricao,
        decimal[]? itemQuantidade,
        decimal[]? itemPrecoUnitario,
        string[]? itemTipo,
        string? observacoes)
    {
        // ── Validacao basica ────────────────────────────────────────────
        if (empresaId == Guid.Empty)
        {
            SetErro("Empresa e obrigatoria.");
            return Page();
        }
        var nomeT = (faturadoNome ?? "").Trim();
        if (nomeT.Length < 2)
        {
            SetErro("Nome do faturado e obrigatorio.");
            return Page();
        }
        if (dataVencimento.Date < DateTime.UtcNow.Date)
        {
            SetErro("Data de vencimento nao pode estar no passado.");
            return Page();
        }

        // Itens (arrays paralelos)
        var totalItens = itemDescricao?.Length ?? 0;
        if (totalItens == 0
            || itemQuantidade == null || itemQuantidade.Length != totalItens
            || itemPrecoUnitario == null || itemPrecoUnitario.Length != totalItens
            || itemTipo == null || itemTipo.Length != totalItens)
        {
            SetErro("Adicione ao menos um item a fatura.");
            return Page();
        }

        var itens = new List<object>(totalItens);
        for (var i = 0; i < totalItens; i++)
        {
            var desc = (itemDescricao![i] ?? "").Trim();
            if (desc.Length is < 2 or > 300)
            {
                SetErro($"Descricao do item #{i + 1} invalida (2-300 caracteres).");
                return Page();
            }
            var tipo = (itemTipo![i] ?? "Servico").Trim();
            if (!TiposItemValidos.Contains(tipo))
            {
                SetErro($"Tipo do item #{i + 1} invalido.");
                return Page();
            }
            if (itemQuantidade![i] <= 0 && !string.Equals(tipo, "Desconto", StringComparison.OrdinalIgnoreCase))
            {
                SetErro($"Quantidade do item #{i + 1} deve ser positiva.");
                return Page();
            }
            if (itemPrecoUnitario![i] < 0)
            {
                SetErro($"Preco do item #{i + 1} nao pode ser negativo.");
                return Page();
            }

            itens.Add(new
            {
                descricao = desc,
                quantidade = itemQuantidade[i],
                precoUnitario = itemPrecoUnitario[i],
                tipo
            });
        }

        // ── Build payload ───────────────────────────────────────────────
        var dadosFaturado = new
        {
            nome = nomeT,
            documento = string.IsNullOrWhiteSpace(faturadoDocumento) ? null : faturadoDocumento.Trim(),
            email = string.IsNullOrWhiteSpace(faturadoEmail) ? null : faturadoEmail.Trim(),
            telefone = string.IsNullOrWhiteSpace(faturadoTelefone) ? null : faturadoTelefone.Trim()
        };

        // DadosEmissor: aceita backend default; admin operacional nao informa.
        var dadosEmissor = new { nome = "EasyStock" };

        var payload = new
        {
            empresaId,
            clienteId = (Guid?)null,
            dadosFaturado,
            dadosEmissor,
            dataVencimento = dataVencimento.ToUniversalTime(),
            itens,
            observacoes,
            dadosFiscais = (object?)null
        };

        try
        {
            var raw = await api.PostAsync<JsonElement>("api/admin/faturas/emitir", payload);
            var faturaId = raw.TryGetProperty("data", out var d) && d.TryGetProperty("faturaId", out var fid)
                ? fid.GetGuid() : Guid.Empty;
            SetSucesso("Fatura emitida com sucesso.");
            if (faturaId != Guid.Empty)
                return RedirectToPage("./Detail", new { id = faturaId });
            return RedirectToPage("./Index");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao emitir fatura avulsa para empresa {EmpresaId}", empresaId);
            SetErro($"Falha ao emitir fatura: {ex.Message}");
            return Page();
        }
    }
}
