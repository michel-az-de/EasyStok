using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Faturas;

public class DetailModel(AdminApiClient api, AdminSessionService session, ILogger<DetailModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public JsonElement FaturaData { get; private set; }
    public string? Erro { get; private set; }

    // ── Helpers para a view ────────────────────────────────────────────────

    public string Numero => Str("numero");
    public string Status => Str("status");
    public string Origem => Str("origem");
    public string Moeda => Str("moeda");
    public string EmpresaNome => Str("empresaNome");
    public string EmpresaId => Str("empresaId");
    public string? Observacoes => StrOrNull("observacoes");
    public string? PdfStorageKey => StrOrNull("pdfStorageKey");
    public string? TicketRelacionadoId => StrOrNull("ticketRelacionadoId");

    public DateTime? DataEmissao => Date("dataEmissao");
    public DateTime? DataVencimento => Date("dataVencimento");
    public DateTime? DataPagamentoTotal => Date("dataPagamentoTotal");

    public decimal SubTotal => Decimal("subTotal");
    public decimal Descontos => Decimal("descontos");
    public decimal Acrescimos => Decimal("acrescimos");
    public decimal Total => Decimal("total");
    public decimal TotalPago => Decimal("totalPago");
    public decimal Pendente => Decimal("pendente");

    public JsonElement DadosFaturado =>
        FaturaData.ValueKind != JsonValueKind.Undefined && FaturaData.TryGetProperty("dadosFaturado", out var v) && v.ValueKind != JsonValueKind.Null
            ? v : default;
    public JsonElement DadosEmissor =>
        FaturaData.ValueKind != JsonValueKind.Undefined && FaturaData.TryGetProperty("dadosEmissor", out var v) && v.ValueKind != JsonValueKind.Null
            ? v : default;

    public IEnumerable<JsonElement> Itens =>
        FaturaData.ValueKind != JsonValueKind.Undefined && FaturaData.TryGetProperty("itens", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public IEnumerable<JsonElement> Pagamentos =>
        FaturaData.ValueKind != JsonValueKind.Undefined && FaturaData.TryGetProperty("pagamentos", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public IEnumerable<JsonElement> Eventos =>
        FaturaData.ValueKind != JsonValueKind.Undefined && FaturaData.TryGetProperty("eventos", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public bool Carregada => FaturaData.ValueKind != JsonValueKind.Undefined;

    private string Str(string k) =>
        FaturaData.ValueKind != JsonValueKind.Undefined && FaturaData.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString() ?? "" : "";

    private string? StrOrNull(string k) =>
        FaturaData.ValueKind != JsonValueKind.Undefined && FaturaData.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString() : null;

    private DateTime? Date(string k)
    {
        if (FaturaData.ValueKind == JsonValueKind.Undefined) return null;
        if (!FaturaData.TryGetProperty(k, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return DateTime.TryParse(v.GetString(), out var d) ? d : (DateTime?)null;
    }

    private decimal Decimal(string k) =>
        FaturaData.ValueKind != JsonValueKind.Undefined && FaturaData.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal() : 0m;

    // ── Handlers ───────────────────────────────────────────────────────────

    public async Task OnGetAsync()
    {
        if (Id == Guid.Empty)
        {
            Erro = "Id invalido.";
            return;
        }

        try
        {
            var raw = await api.GetRawAsync($"api/admin/faturas/{Id}");
            FaturaData = raw.TryGetProperty("data", out var d) ? d : default;
        }
        catch (SessionExpiredException) { throw; }
        catch (ApiException apiEx) when (apiEx.HttpStatus == 404)
        {
            Erro = "Fatura nao encontrada.";
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar fatura {FaturaId}", Id);
            Erro = "Falha ao carregar a fatura. Tente recarregar.";
        }
    }

    /// <summary>GET handler que serve o PDF via proxy (browser baixa direto).</summary>
    public async Task<IActionResult> OnGetPdfAsync()
    {
        if (Id == Guid.Empty) return RedirectToPage(new { Id });

        try
        {
            var (bytes, contentType) = await api.GetBytesAsync($"api/admin/faturas/{Id}/pdf");
            return File(bytes, contentType ?? "application/pdf", $"fatura-{Id:N}.pdf");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao baixar PDF da fatura {FaturaId}", Id);
            SetErro($"Falha ao baixar PDF: {ex.Message}");
            return RedirectToPage(new { Id });
        }
    }

    public async Task<IActionResult> OnPostMarcarPagaAsync(decimal valor, string? metodo, string? observacao)
    {
        if (Id == Guid.Empty)
        {
            SetErro("Fatura invalida.");
            return RedirectToPage(new { Id });
        }
        if (valor <= 0)
        {
            SetErro("Valor do pagamento deve ser maior que zero.");
            return RedirectToPage(new { Id });
        }
        var metodoT = string.IsNullOrWhiteSpace(metodo) ? "manual" : metodo.Trim().ToLowerInvariant();

        try
        {
            await api.PostAsync<JsonElement>($"api/admin/faturas/{Id}/pagamentos",
                new { valor, metodo = metodoT, gatewayProvedor = "Manual", observacao });
            SetSucesso("Pagamento registrado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao registrar pagamento na fatura {FaturaId}", Id);
            SetErro($"Falha ao registrar pagamento: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostAbrirTicketAsync(string titulo, string descricao, string? prioridade, string? nivel)
    {
        if (Id == Guid.Empty)
        {
            SetErro("Fatura invalida.");
            return RedirectToPage(new { Id });
        }
        var tituloT = (titulo ?? "").Trim();
        var descricaoT = (descricao ?? "").Trim();
        if (tituloT.Length is < 3 or > 200)
        {
            SetErro("Titulo deve ter entre 3 e 200 caracteres.");
            return RedirectToPage(new { Id });
        }
        if (descricaoT.Length is < 5 or > 4000)
        {
            SetErro("Descricao deve ter entre 5 e 4000 caracteres.");
            return RedirectToPage(new { Id });
        }

        // Buscar empresaId da fatura para criar o ticket no contexto correto.
        var fatura = await api.GetRawAsync($"api/admin/faturas/{Id}");
        var empresaId = fatura.TryGetProperty("data", out var d) && d.TryGetProperty("empresaId", out var eid)
            ? eid.GetGuid() : Guid.Empty;
        if (empresaId == Guid.Empty)
        {
            SetErro("Nao foi possivel resolver a empresa da fatura.");
            return RedirectToPage(new { Id });
        }

        try
        {
            await api.PostAsync<JsonElement>("api/admin/tickets", new
            {
                empresaId,
                titulo = tituloT,
                descricao = descricaoT,
                categoria = "Financeiro",
                prioridade = string.IsNullOrWhiteSpace(prioridade) ? "Normal" : prioridade.Trim(),
                nivel = string.IsNullOrWhiteSpace(nivel) ? "N1" : nivel.Trim(),
                faturaId = Id
            });
            SetSucesso("Ticket interno aberto.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao abrir ticket interno sobre fatura {FaturaId}", Id);
            SetErro($"Falha ao abrir ticket: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostCancelarAsync(string? motivo)
    {
        if (Id == Guid.Empty)
        {
            SetErro("Fatura invalida.");
            return RedirectToPage(new { Id });
        }

        try
        {
            await api.PostAsync<JsonElement>($"api/admin/faturas/{Id}/cancelar", new { motivo });
            SetSucesso("Fatura cancelada.");
            return RedirectToPage(new { Id });
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao cancelar fatura {FaturaId}", Id);
            SetErro($"Falha ao cancelar: {ex.Message}");
            return RedirectToPage(new { Id });
        }
    }
}
