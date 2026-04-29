using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>
/// Cliente HTTP pra <c>/api/mobile/products/*</c> (Onda 2).
/// Painel <c>/produtos-mobile</c> usa pra revisar produtos custom criados
/// no app e aprovar/linkar com produtos ERP existentes.
/// </summary>
public class MobileProductsService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<MobileProductApi>>> ListarPendentesAsync() =>
        api.GetAsync<List<MobileProductApi>>(
            $"mobile/products?empresaId={GetEmpresaId()}&pendingOnly=true");

    public Task<ApiResult<List<MobileProductApi>>> ListarTodosCustomAsync() =>
        api.GetAsync<List<MobileProductApi>>(
            $"mobile/products?empresaId={GetEmpresaId()}&customOnly=true");

    public Task<ApiResult<bool>> AprovarAsync(string id) =>
        api.PostAsync<bool>($"mobile/products/{id}/approve", new { });

    public Task<ApiResult<bool>> LinkarAsync(string id, Guid erpProductId) =>
        api.PostAsync<bool>($"mobile/products/{id}/link", new { erpProductId });

    public Task<ApiResult<bool>> DesfazerAsync(string id) =>
        api.PostAsync<bool>($"mobile/products/{id}/unlink", new { });

    /// <summary>Lista divergências de estoque (Onda 2 parte 2).</summary>
    public Task<ApiResult<List<StockDivergenceApi>>> ListarDivergenciasAsync() =>
        api.GetAsync<List<StockDivergenceApi>>(
            $"mobile/stock/divergences?empresaId={GetEmpresaId()}");

    /// <summary>Reconcilia 1 produto — força mobile.Stock = ERP.Quantidade.</summary>
    public Task<ApiResult<object>> ReconciliarAsync(string id) =>
        api.PostAsync<object>($"mobile/products/{id}/reconcile-stock", new { });
}

/// <summary>Item da aba Divergências.</summary>
public class StockDivergenceApi
{
    public string MobileProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? Emoji { get; set; }
    public Guid ErpProductId { get; set; }
    public Guid? LojaId { get; set; }
    public int MobileStock { get; set; }
    public int ErpStock { get; set; }
    public int Delta { get; set; }
    public bool ItemEstoqueExists { get; set; }
    public string? LastDeviceId { get; set; }
    public string? LastOperatorName { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Resposta do GET /api/mobile/products.</summary>
public class MobileProductApi
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Emoji { get; set; }
    public string Category { get; set; } = "";
    public string? Unit { get; set; }
    public decimal? Price { get; set; }
    public int Stock { get; set; }
    public bool IsCustom { get; set; }
    public bool IsApproved { get; set; }
    public Guid? ErpProductId { get; set; }
    public Guid? EmpresaId { get; set; }
    public Guid? LojaId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? LastDeviceId { get; set; }
    public string? LastOperatorName { get; set; }
}
