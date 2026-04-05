using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class FornecedoresService(ApiClient api)
{
    public Task<ApiResult<List<Fornecedor>>> ListarAsync(string? status = null, string? search = null)
    {
        var qs = "fornecedores";
        var sep = "?";
        if (!string.IsNullOrEmpty(status)) { qs += $"{sep}status={Uri.EscapeDataString(status)}"; sep = "&"; }
        if (!string.IsNullOrEmpty(search)) qs += $"{sep}search={Uri.EscapeDataString(search)}";
        return api.GetAsync<List<Fornecedor>>(qs);
    }

    public Task<ApiResult<Fornecedor>> ObterAsync(string id) =>
        api.GetAsync<Fornecedor>($"fornecedores/{id}");

    public Task<ApiResult<Fornecedor>> CriarAsync(object body) =>
        api.PostAsync<Fornecedor>("fornecedores", body);

    public Task<ApiResult<object>> EditarAsync(string id, object body) =>
        api.PatchAsync<object>($"fornecedores/{id}", body);

    public Task<ApiResult<bool>> ExcluirAsync(string id) =>
        api.DeleteAsync($"fornecedores/{id}");

    public Task<ApiResult<List<PedidoFornecedor>>> ListarPedidosAbertosAsync()
    {
        return api.GetAsync<List<PedidoFornecedor>>("fornecedores/pedidos?status=aberto,em_transito");
    }

    public Task<ApiResult<object>> CriarPedidoAsync(string fornId, object body) =>
        api.PostAsync<object>($"fornecedores/{fornId}/pedidos", body);

    public Task<ApiResult<object>> AlterarStatusPedidoAsync(string fornId, string pedId, string novoStatus) =>
        api.PatchAsync<object>($"fornecedores/{fornId}/pedidos/{pedId}/status", new { status = novoStatus });

    public Task<ApiResult<object>> ReceberPedidoAsync(string fornId, string pedId, object body) =>
        api.PostAsync<object>($"fornecedores/{fornId}/pedidos/{pedId}/receber", body);

    public Task<ApiResult<bool>> CancelarPedidoAsync(string fornId, string pedId) =>
        api.DeleteAsync($"fornecedores/{fornId}/pedidos/{pedId}");
}
