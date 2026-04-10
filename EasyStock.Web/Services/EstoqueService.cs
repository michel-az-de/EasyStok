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

            var searchItems = buscarResult.Data ?? [];
            return ApiResult<PagedResult<EstoqueSku>>.Ok(new PagedResult<EstoqueSku>
            {
                Data = searchItems,
                Meta = new Meta(searchItems.Count, 1, 1, searchItems.Count)
            });
        }

        var result = await api.GetAsync<PagedResult<EstoqueSku>>($"estoque?page={page}&pageSize=200");
        if (!result.Success || result.Data is null)
            return result;

        var items = result.Data.Data;

        // Client-side status filter (API doesn't support it natively)
        if (!string.IsNullOrEmpty(status))
        {
            items = status.ToLower() switch
            {
                "ok" or "normal" => items.Where(i => i.Status is "ok" or "OK" or "Normal" or "normal").ToList(),
                "atencao" => items.Where(i => i.Status is "atencao" or "Atenção" or "atenção" or "Atencao").ToList(),
                "critico" => items.Where(i => i.Status is "critico" or "Crítico" or "crítico" or "Critico").ToList(),
                "parado" => items.Where(i => i.Status is "parado" or "Parado").ToList(),
                _ => items
            };
        }

        // Client-side category filter
        if (!string.IsNullOrEmpty(categoria))
        {
            items = items.Where(i =>
                i.Produto?.Categoria != null &&
                (i.Produto.Categoria.Equals(categoria, StringComparison.OrdinalIgnoreCase) ||
                 i.ProdutoId == categoria)).ToList();
        }

        // Re-paginate filtered results
        const int pageSize = 20;
        var total = items.Count;
        var pages = (int)Math.Ceiling(total / (double)pageSize);
        var pagedItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return ApiResult<PagedResult<EstoqueSku>>.Ok(new PagedResult<EstoqueSku>
        {
            Data = pagedItems,
            Meta = new Meta(total, page, pages, pageSize)
        });
    }

    public Task<ApiResult<EstoqueSku>> ObterAsync(string id) =>
        api.GetAsync<EstoqueSku>($"estoque/{id}");

    public Task<ApiResult<List<EstoqueSku>>> ObterItensPorProdutoAsync(string produtoId) =>
        api.GetAsync<List<EstoqueSku>>($"estoque/por-produto/{produtoId}");

    public Task<ApiResult<ProdutoDetalhe>> ObterProdutoDetalheAsync(string id) =>
        api.GetAsync<ProdutoDetalhe>($"produtos/{id}");

    public Task<ApiResult<PagedResult<EstoqueSku>>> AlertasAsync() =>
        api.GetAsync<PagedResult<EstoqueSku>>("estoque?pageSize=20");

    public async Task<ApiResult<PagedResult<EstoqueSku>>> ExportarAsync()
    {
        const int pageSize = 1000;
        var page = 1;
        var items = new List<EstoqueSku>();

        while (true)
        {
            var result = await api.GetAsync<PagedResult<EstoqueSku>>($"estoque?page={page}&pageSize={pageSize}");
            if (!result.Success)
                return ApiResult<PagedResult<EstoqueSku>>.Fail(
                    result.ErrorCode!, result.ErrorMessage!, result.HttpStatus);

            var currentItems = result.Data?.Data ?? [];
            if (currentItems.Count == 0)
                break;

            items.AddRange(currentItems);

            if (currentItems.Count < pageSize)
                break;

            page++;
        }

        return ApiResult<PagedResult<EstoqueSku>>.Ok(new PagedResult<EstoqueSku>
        {
            Data = items,
            Meta = new Meta(items.Count, 1, 1, items.Count)
        });
    }
}
