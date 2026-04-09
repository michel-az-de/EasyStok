using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class EstoqueService(ApiClient api)
{
    public async Task<ApiResult<PagedResult<EstoqueSku>>> ListarAsync(
        int page = 1, string? status = null, string? categoria = null, string? search = null)
    {
        // When a search term is supplied, use the dedicated full-text search endpoint.
        // The regular GetAll endpoint does not support status/categoria/termo filters.
        if (!string.IsNullOrEmpty(search))
        {
            var buscarQs = $"estoque/buscar?termo={Uri.EscapeDataString(search)}&limite=100";
            var buscarResult = await api.GetAsync<List<EstoqueSku>>(buscarQs);
            if (!buscarResult.Success)
                return ApiResult<PagedResult<EstoqueSku>>.Fail(
                    buscarResult.ErrorCode!, buscarResult.ErrorMessage!, buscarResult.HttpStatus);

            var items = buscarResult.Data ?? [];
            return ApiResult<PagedResult<EstoqueSku>>.Ok(new PagedResult<EstoqueSku>
            {
                Data = items,
                Meta = new Meta(items.Count, 1, 1, items.Count)
            });
        }

        return await api.GetAsync<PagedResult<EstoqueSku>>($"estoque?page={page}&pageSize=20");
    }

    public Task<ApiResult<EstoqueSku>> ObterAsync(string id) =>
        api.GetAsync<EstoqueSku>($"estoque/{id}");

    public Task<ApiResult<PagedResult<EstoqueSku>>> AlertasAsync() =>
        api.GetAsync<PagedResult<EstoqueSku>>("estoque?pageSize=20");
}
