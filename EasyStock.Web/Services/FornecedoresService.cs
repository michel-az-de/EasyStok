using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class FornecedoresService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<Fornecedor>>> ListarAsync(string? status = null, string? search = null)
    {
        var qs = $"fornecedores?empresaId={GetEmpresaId()}&page=1&pageSize=200";
        if (status == "ativo") qs += "&ativo=true";
        else if (status == "inativo") qs += "&ativo=false";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync<List<Fornecedor>>(qs);
    }

    public Task<ApiResult<Fornecedor>> ObterAsync(string id) =>
        api.GetAsync<Fornecedor>($"fornecedores/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<Fornecedor>> CriarAsync(
        string nome, string? documento, string? contato, string? email, string? telefone,
        int? leadTimeEstimadoDias, string? tipo, string? categoria, string? siteUrl,
        string? pedidoMinimo, string? fretePadrao, string? observacoes) =>
        api.PostAsync<Fornecedor>("fornecedores", new
        {
            empresaId = GetEmpresaId(),
            nome, documento, contato, email, telefone, leadTimeEstimadoDias,
            tipo, categoria, siteUrl, pedidoMinimo, fretePadrao, observacoes
        });

    public Task<ApiResult<object>> EditarAsync(string id,
        string nome, string? documento, string? contato, string? email, string? telefone,
        int? leadTimeEstimadoDias, string? tipo, string? categoria, string? siteUrl,
        string? pedidoMinimo, string? fretePadrao, string? observacoes) =>
        api.PatchAsync<object>($"fornecedores/{id}", new
        {
            fornecedorId = Guid.TryParse(id, out var fid) ? fid : Guid.Empty,
            empresaId = GetEmpresaId(),
            nome, documento, contato, email, telefone, leadTimeEstimadoDias,
            tipo, categoria, siteUrl, pedidoMinimo, fretePadrao, observacoes
        });

    public Task<ApiResult<bool>> ExcluirAsync(string id) =>
        api.DeleteAsync($"fornecedores/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<FornecedorHistoricoItem>>> ObterHistoricoAsync(string id) =>
        api.GetAsync<List<FornecedorHistoricoItem>>($"fornecedores/{id}/historico?empresaId={GetEmpresaId()}");

    public Task<ApiResult<FornecedorEstatisticas>> ObterEstatisticasAsync(string id) =>
        api.GetAsync<FornecedorEstatisticas>($"fornecedores/{id}/estatisticas?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<PedidoAberto>>> ListarPedidosAbertosAsync() =>
        api.GetAsync<List<PedidoAberto>>($"fornecedores/pedidos-abertos?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> CriarPedidoAsync(
        string fornecedorId, DateOnly dataPedido, DateOnly? previsaoEntrega,
        decimal? valorEstimado, string? canal, string? observacoes) =>
        api.PostAsync<object>("fornecedores/pedidos", new
        {
            empresaId = GetEmpresaId(),
            fornecedorId = Guid.TryParse(fornecedorId, out var fid) ? fid : Guid.Empty,
            dataPedido = dataPedido.ToDateTime(TimeOnly.MinValue),
            previsaoEntrega = previsaoEntrega.HasValue ? previsaoEntrega.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
            valorEstimado,
            canal,
            observacoes
        });

    public Task<ApiResult<object>> ReceberPedidoAsync(string pedidoId, string? tracking) =>
        api.PatchAsync<object>($"fornecedores/pedidos/{pedidoId}/receber", new
        {
            empresaId = GetEmpresaId(),
            dataRecebimento = DateTime.UtcNow,
            tracking
        });

    public Task<ApiResult<object>> CancelarPedidoAsync(string pedidoId) =>
        api.PatchAsync<object>($"fornecedores/pedidos/{pedidoId}/cancelar", new
        {
            empresaId = GetEmpresaId()
        });
}
