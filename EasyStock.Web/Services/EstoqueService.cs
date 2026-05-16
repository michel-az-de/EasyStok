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

            // Aplicar filtro de status sobre os resultados de busca (client-side)
            if (!string.IsNullOrEmpty(status))
                searchItems = searchItems
                    .Where(i => string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            return ApiResult<PagedResult<EstoqueSku>>.Ok(new PagedResult<EstoqueSku>
            {
                Data = searchItems,
                Meta = new Meta(searchItems.Count, 1, 1, searchItems.Count)
            });
        }

        var qs = $"estoque?empresaId={GetEmpresaId()}&page={page}&pageSize=20";
        if (!string.IsNullOrEmpty(status))
            qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(categoria) && Guid.TryParse(categoria, out var catId))
            qs += $"&categoriaId={catId}";

        var result = await api.GetAsync<PagedResult<EstoqueSku>>(qs);

        // Defesa em profundidade: a API estava devolvendo itens que nao batiam
        // com o filtro de status (ex: pill "Critico" mostrava itens com badge
        // "Atencao"). Aqui aplicamos um segundo filtro client-side espelhando
        // a logica que a view usa para renderizar o badge — assim filtro e
        // badge sempre concordam visualmente, ate o backend corrigir.
        if (!string.IsNullOrEmpty(status)
            && result.Success
            && result.Data is { Data: { } items } paged
            && items.Count > 0)
        {
            var filtered = items.Where(i => MatchesStatusFilter(i, status)).ToList();
            if (filtered.Count != items.Count)
            {
                return ApiResult<PagedResult<EstoqueSku>>.Ok(new PagedResult<EstoqueSku>
                {
                    Data = filtered,
                    Meta = new Meta(filtered.Count, paged.Meta.Page, 1, filtered.Count)
                });
            }
        }

        return result;
    }

    // Espelha o switch da view Estoque/Index.cshtml (statusEffective). Qty 0
    // override para "esgotado" — mesmo se Status no banco veio outra coisa.
    private static bool MatchesStatusFilter(EstoqueSku item, string filterStatus)
    {
        var effective = item.Qty == 0
            ? "esgotado"
            : (item.Status ?? string.Empty).ToLowerInvariant();
        var filter = filterStatus.ToLowerInvariant();

        return filter switch
        {
            "ok"       => effective is "ok" or "normal",
            "critico"  => effective is "critico" or "crítico" or "critical",
            "atencao"  => effective is "atencao" or "warn",
            "parado"   => effective is "parado" or "slow",
            "esgotado" => effective is "esgotado",
            _ => effective == filter
        };
    }

    public async Task<ApiResult<EstoqueContadores>> ContadoresAsync(string? status = null, string? categoria = null)
    {
        var qs = $"estoque/contadores?empresaId={GetEmpresaId()}";
        if (!string.IsNullOrEmpty(status))
            qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(categoria) && Guid.TryParse(categoria, out var catId))
            qs += $"&categoriaId={catId}";
        return await api.GetAsync<EstoqueContadores>(qs);
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
