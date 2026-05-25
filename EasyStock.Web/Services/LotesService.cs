using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>Onda P5.A — UI Web do módulo Lote.</summary>
public class LotesService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private static ApiResult<T> EmpresaErr<T>() =>
        ApiResult<T>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");

    public Task<ApiResult<List<Lote>>> ListarAsync(string? status = null, string? search = null)
    {
        var qs = $"lotes?empresaId={GetEmpresaId()}&page=1&pageSize=100";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync<List<Lote>>(qs);
    }

    public Task<ApiResult<LoteDetalhe>> ObterAsync(string id) =>
        api.GetAsync<LoteDetalhe>($"lotes/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<Lote>> CriarAsync(string? codigo, string? operadorNome, string? observacoes, List<CriarLoteItemInput>? itens)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Lote>());
        return api.PostAsync<Lote>("lotes", new
        {
            empresaId,
            codigoCustom = codigo,
            operadorNome,
            observacoes,
            origem = "web",
            itens
        });
    }

    public Task<ApiResult<Lote>> AdicionarItemAsync(string id,
        string nome, int quantidade, Guid? produtoId, string? emoji, string? unidade,
        int? pesoG, int? validadeDias)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Lote>());
        return api.PostAsync<Lote>($"lotes/{id}/itens", new
        {
            empresaId,
            loteId = Guid.Parse(id),
            nome,
            quantidade,
            produtoId,
            emoji,
            unidade,
            pesoG,
            validadeDias
        });
    }

    public Task<ApiResult<bool>> RemoverItemAsync(string id, string itemId) =>
        api.DeleteAsync($"lotes/{id}/itens/{itemId}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<Lote>> FinalizarAsync(string id) =>
        api.PostAsync<Lote>($"lotes/{id}/finalizar?empresaId={GetEmpresaId()}", new { });

    /// <summary>
    /// C2 (R3 + backfill): atualiza peso de item de lote em producao.
    /// API bloqueia se lote ja finalizado.
    /// </summary>
    public Task<ApiResult<object>> AtualizarPesoItemAsync(string loteId, string itemId, int pesoG) =>
        api.PatchAsync<object>($"lotes/{loteId}/itens/{itemId}/peso?empresaId={GetEmpresaId()}",
            new { pesoG });

    /// <summary>C2 (R10): lista lotes com pelo menos 1 item embalado sem peso.</summary>
    public Task<ApiResult<List<LotePendentePesoDto>>> ListarPendentesPesoAsync() =>
        api.GetAsync<List<LotePendentePesoDto>>($"lotes/pendentes-peso?empresaId={GetEmpresaId()}");

    // ── Mobile ──────────────────────────────────────────────
    public Task<ApiResult<List<MobileBatchSummary>>> ListarMobileAsync(bool pendingOnly = false)
    {
        var qs = $"mobile/batches?empresaId={GetEmpresaId()}";
        if (pendingOnly) qs += "&pendingOnly=true";
        return api.GetAsync<List<MobileBatchSummary>>(qs);
    }

    public Task<ApiResult<object>> LinkMobileAsync(string mobileBatchId, Guid? erpLoteId) =>
        api.PostAsync<object>($"mobile/batches/{mobileBatchId}/link", new { erpLoteId });

    public Task<ApiResult<object>> UnlinkMobileAsync(string mobileBatchId) =>
        api.PostAsync<object>($"mobile/batches/{mobileBatchId}/unlink", new { });
}

public record CriarLoteItemInput(string Nome, int Quantidade,
    Guid? ProdutoId = null, string? Emoji = null, string? Unidade = null,
    int? PesoG = null, int? ValidadeDias = null, string? FotoUrl = null);

/// <summary>C2 (R10): item retornado por /api/lotes/pendentes-peso.</summary>
public record LotePendentePesoDto(Guid Id, string Codigo, DateTime DataProducao, int ItensPendentes);
