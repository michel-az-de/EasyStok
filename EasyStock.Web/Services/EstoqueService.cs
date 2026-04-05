using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class EstoqueService(ApiClient api)
{
    public Task<ApiResult<PagedResult<EstoqueSku>>> ListarAsync(
        int page = 1, string? status = null, string? categoria = null, string? search = null)
    {
        var qs = $"estoque?page={page}&pageSize=20";
        if (!string.IsNullOrEmpty(status) && status != "todos") qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(categoria)) qs += $"&categoria={Uri.EscapeDataString(categoria)}";
        if (!string.IsNullOrEmpty(search)) qs += $"&termo={Uri.EscapeDataString(search)}";
        return api.GetAsync<PagedResult<EstoqueSku>>(qs);
    }

    public Task<ApiResult<EstoqueSku>> ObterAsync(string id) =>
        api.GetAsync<EstoqueSku>($"estoque/{id}");

    public Task<ApiResult<PagedResult<EstoqueSku>>> AlertasAsync() =>
        api.GetAsync<PagedResult<EstoqueSku>>("estoque?status=critico&pageSize=20");
}
