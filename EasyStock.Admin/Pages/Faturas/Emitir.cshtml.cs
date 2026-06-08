namespace EasyStock.Admin.Pages.Faturas;

/// <summary>
/// Emissão de fatura avulsa pelo admin. Aceita arrays paralelos de itens
/// montados pelo form Alpine (itemDescricao[], itemQuantidade[], etc.).
/// </summary>
public class EmitirModel(AdminApiClient api, AdminSessionService session, ILogger<EmitirModel> log) : AdminPageBase(session)
{
    public string? Erro { get; private set; }

    // Repopulacao no postback de erro (BUG-03): antes os campos do cliente eram
    // perdidos porque o <es-input> nao relê do request. Agora o PageModel expoe
    // os valores submetidos e a view os reinjeta via value="@Model...".
    public string? EmpresaIdRaw { get; private set; }
    public string? FaturadoNome { get; private set; }
    public string? FaturadoDocumento { get; private set; }
    public string? FaturadoTelefone { get; private set; }
    public string? FaturadoEmail { get; private set; }
    public string? ObservacoesRaw { get; private set; }

    /// <summary>
    /// Itens submetidos como JSON, para reidratar o Alpine no postback. Serializado
    /// com o encoder default do System.Text.Json (escapa &lt; &gt; &amp; ' "), seguro
    /// para embutir em &lt;script type="application/json"&gt; via @Html.Raw (G2).
    /// </summary>
    public string ItensJson { get; private set; } = "[]";

    private static readonly HashSet<string> TiposItemValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Produto", "Servico", "Recorrencia", "Desconto", "Taxa" };

    public void OnGet() { }

    /// <summary>
    /// Aceita arrays paralelos para itens (dinamicos no form via Alpine).
    /// empresaId chega como string (nao Guid) para preservar o texto invalido
    /// no postback de erro (BUG-03/05) e validar com mensagem clara.
    /// </summary>
    public async Task<IActionResult> OnPostAsync(
        string? empresaId,
        string? faturadoNome,
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
        // ── Repopulacao (BUG-03): preserva tudo que o usuario digitou, ANTES de
        //    qualquer return Page() por erro de validacao.
        EmpresaIdRaw = empresaId;
        FaturadoNome = faturadoNome;
        FaturadoDocumento = faturadoDocumento;
        FaturadoTelefone = faturadoTelefone;
        FaturadoEmail = faturadoEmail;
        ObservacoesRaw = observacoes;
        ItensJson = SerializarItens(itemDescricao, itemQuantidade, itemPrecoUnitario, itemTipo);

        // ── Validacao basica ────────────────────────────────────────────
        if (!Guid.TryParse(empresaId, out var empresaGuid) || empresaGuid == Guid.Empty)
        {
            SetErro("Empresa ID invalido — cole um GUID valido da pagina /Tenants.");
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
            empresaId = empresaGuid,
            clienteId = (Guid?)null,
            dadosFaturado,
            dadosEmissor,
            // Vencimento e data civil: SpecifyKind(Utc) preserva 14/06 como 14/06T00:00Z
            // independente do fuso do servidor (ToUniversalTime trataria como hora local). G4.
            dataVencimento = DateTime.SpecifyKind(dataVencimento, DateTimeKind.Utc),
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
            log.LogError(ex, "Falha ao emitir fatura avulsa para empresa {EmpresaId}", empresaGuid);
            SetErro($"Falha ao emitir fatura: {ex.Message}");
            return Page();
        }
    }

    /// <summary>
    /// Serializa os arrays paralelos no shape que o factory Alpine espera
    /// (descricao/quantidade/precoUnitario/tipo). Encoder default = seguro p/ embed (G2).
    /// </summary>
    private static string SerializarItens(string[]? desc, decimal[]? qtd, decimal[]? preco, string[]? tipo)
    {
        var n = desc?.Length ?? 0;
        if (n == 0) return "[]";
        var lista = new List<object>(n);
        for (var i = 0; i < n; i++)
        {
            lista.Add(new
            {
                descricao = desc![i] ?? "",
                quantidade = (qtd != null && i < qtd.Length) ? qtd[i] : 1m,
                precoUnitario = (preco != null && i < preco.Length) ? preco[i] : 0m,
                tipo = (tipo != null && i < tipo.Length) ? (tipo[i] ?? "Servico") : "Servico"
            });
        }
        return JsonSerializer.Serialize(lista);
    }
}
