using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class ListasComprasService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private static ApiResult<T> EmpresaErr<T>() =>
        ApiResult<T>.Fail("EMPRESA_INVALIDA", "Loja não identificada.");

    public Task<ApiResult<List<ListaCompras>>> ListarAsync(string? status = null) =>
        api.GetAsync<List<ListaCompras>>(
            $"listas-compras?empresaId={GetEmpresaId()}&page=1&pageSize=100" +
            (status == null ? "" : $"&status={Uri.EscapeDataString(status)}"));

    public Task<ApiResult<ListaComprasDetalhe>> ObterAsync(string id) =>
        api.GetAsync<ListaComprasDetalhe>($"listas-compras/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<ListaCompras>> CriarAsync(string nome, string? observacoes)
    {
        var emp = GetEmpresaId();
        if (emp == Guid.Empty) return Task.FromResult(EmpresaErr<ListaCompras>());
        return api.PostAsync<ListaCompras>("listas-compras", new
        {
            empresaId = emp, nome, observacoes,
            criadaPorNome = session.GetUsuarioNome(),
            origem = "web"
        });
    }

    public Task<ApiResult<ListaCompras>> GerarAsync(string nome, string? observacoes, IEnumerable<object> itens)
    {
        var emp = GetEmpresaId();
        if (emp == Guid.Empty) return Task.FromResult(EmpresaErr<ListaCompras>());
        return api.PostAsync<ListaCompras>("listas-compras/gerar", new
        {
            empresaId = emp, nome, observacoes,
            criadaPorNome = session.GetUsuarioNome(),
            origem = "web",
            itens
        });
    }

    public Task<ApiResult<GerarPedidosResultApi>> GerarPedidosAsync(string id)
    {
        var emp = GetEmpresaId();
        if (emp == Guid.Empty) return Task.FromResult(EmpresaErr<GerarPedidosResultApi>());
        var loja = session.GetLojaId();
        var lojaParam = string.IsNullOrEmpty(loja) ? "" : $"&lojaId={loja}";
        return api.PostAsync<GerarPedidosResultApi>(
            $"listas-compras/{id}/gerar-pedidos?empresaId={emp}{lojaParam}", new { });
    }

    public Task<ApiResult<ListaCompras>> ArquivarAsync(string id) =>
        api.PostAsync<ListaCompras>($"listas-compras/{id}/arquivar?empresaId={GetEmpresaId()}", new { });

    public Task<ApiResult<ListaCompras>> ReabrirAsync(string id) =>
        api.PostAsync<ListaCompras>($"listas-compras/{id}/reabrir?empresaId={GetEmpresaId()}", new { });

    public Task<ApiResult<object>> AddItemAsync(string id,
        string texto, decimal? quantidade, string? unidade, string? categoria, string? observacao, Guid? produtoId = null)
    {
        var emp = GetEmpresaId();
        if (emp == Guid.Empty) return Task.FromResult(EmpresaErr<object>());
        return api.PostAsync<object>($"listas-compras/{id}/itens", new
        {
            empresaId = emp, listaComprasId = Guid.Parse(id),
            texto, quantidade, unidade, categoria, observacao, produtoId
        });
    }

    public Task<ApiResult<object>> ToggleItemAsync(string id, string itemId, bool done)
    {
        var emp = GetEmpresaId();
        if (emp == Guid.Empty) return Task.FromResult(EmpresaErr<object>());
        return api.PatchAsync<object>($"listas-compras/{id}/itens/{itemId}", new
        {
            empresaId = emp, done, usuarioNome = session.GetUsuarioNome()
        });
    }

    public Task<ApiResult<bool>> RemoveItemAsync(string id, string itemId) =>
        api.DeleteAsync($"listas-compras/{id}/itens/{itemId}?empresaId={GetEmpresaId()}");
}
