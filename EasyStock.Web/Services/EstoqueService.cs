using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class EstoqueService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public async Task<ApiResult<PagedResult<EstoqueSku>>> ListarAsync(
        int page = 1, string? status = null, string? categoria = null, string? search = null)
    {
        if (!string.IsNullOrEmpty(search))
        {
            var buscarQs = $"estoque/buscar?empresaId={GetEmpresaId()}&termo={Uri.EscapeDataString(search)}&limite=100";
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

        var qs = $"estoque?empresaId={GetEmpresaId()}&page={page}&pageSize=20";
        if (!string.IsNullOrEmpty(status))
            qs += $"&status={Uri.EscapeDataString(status)}";

        return await api.GetAsync<PagedResult<EstoqueSku>>(qs);
    }

    public Task<ApiResult<EstoqueSku>> ObterAsync(string id) =>
        api.GetAsync<EstoqueSku>($"estoque/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<EstoqueSku>>> ObterItensPorProdutoAsync(string produtoId) =>
        api.GetAsync<List<EstoqueSku>>($"estoque/por-produto/{produtoId}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<ProdutoDetalhe>> ObterProdutoDetalheAsync(string id) =>
        api.GetAsync<ProdutoDetalhe>($"produtos/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<PagedResult<EstoqueSku>>> AlertasAsync() =>
        api.GetAsync<PagedResult<EstoqueSku>>($"estoque?empresaId={GetEmpresaId()}&pageSize=20&status=critico");

    public async Task<ApiResult<PagedResult<EstoqueSku>>> ExportarAsync()
    {
        const int pageSize = 1000;
        var page = 1;
        var items = new List<EstoqueSku>();

        while (true)
        {
            var result = await api.GetAsync<PagedResult<EstoqueSku>>($"estoque?empresaId={GetEmpresaId()}&page={page}&pageSize={pageSize}");
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
