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
        string? pedidoMinimo, string? fretePadrao, string? observacoes)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return Task.FromResult(ApiResult<Fornecedor>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente."));

        return api.PostAsync<Fornecedor>("fornecedores", new
        {
            empresaId, nome, documento, contato, email, telefone, leadTimeEstimadoDias,
            tipo, categoria, siteUrl, pedidoMinimo, fretePadrao, observacoes
        });
    }

    public Task<ApiResult<object>> EditarAsync(string id,
        string nome, string? documento, string? contato, string? email, string? telefone,
        int? leadTimeEstimadoDias, string? tipo, string? categoria, string? siteUrl,
        string? pedidoMinimo, string? fretePadrao, string? observacoes)
    {
        if (!Guid.TryParse(id, out var fid))
            return Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "ID de fornecedor inválido."));

        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return Task.FromResult(ApiResult<object>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente."));

        return api.PatchAsync<object>($"fornecedores/{id}", new
        {
            fornecedorId = fid,
            empresaId, nome, documento, contato, email, telefone, leadTimeEstimadoDias,
            tipo, categoria, siteUrl, pedidoMinimo, fretePadrao, observacoes
        });
    }

    public Task<ApiResult<bool>> ExcluirAsync(string id)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return Task.FromResult(ApiResult<bool>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente."));
        return api.DeleteAsync($"fornecedores/{id}?empresaId={empresaId}");
    }

    public Task<ApiResult<List<FornecedorHistoricoItem>>> ObterHistoricoAsync(string id) =>
        api.GetAsync<List<FornecedorHistoricoItem>>($"fornecedores/{id}/historico?empresaId={GetEmpresaId()}");

    public Task<ApiResult<FornecedorEstatisticas>> ObterEstatisticasAsync(string id) =>
        api.GetAsync<FornecedorEstatisticas>($"fornecedores/{id}/estatisticas?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<PedidoAberto>>> ListarPedidosAbertosAsync() =>
        api.GetAsync<List<PedidoAberto>>($"fornecedores/pedidos-abertos?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> CriarPedidoAsync(
        string fornecedorId, DateOnly dataPedido, DateOnly? previsaoEntrega,
        decimal? valorEstimado, string? canal, string? observacoes)
    {
        if (!Guid.TryParse(fornecedorId, out var fid))
            return Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "ID de fornecedor inválido."));

        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return Task.FromResult(ApiResult<object>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente."));

        return api.PostAsync<object>("fornecedores/pedidos", new
        {
            empresaId,
            fornecedorId = fid,
            dataPedido = dataPedido.ToDateTime(TimeOnly.MinValue),
            previsaoEntrega = previsaoEntrega.HasValue ? previsaoEntrega.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
            valorEstimado,
            canal,
            observacoes
        });
    }

    public Task<ApiResult<object>> ReceberPedidoAsync(string pedidoId, string? tracking)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return Task.FromResult(ApiResult<object>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente."));

        return api.PatchAsync<object>($"fornecedores/pedidos/{pedidoId}/receber", new
        {
            empresaId,
            dataRecebimento = DateTime.UtcNow,
            tracking
        });
    }

    public Task<ApiResult<object>> CancelarPedidoAsync(string pedidoId)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return Task.FromResult(ApiResult<object>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente."));

        return api.PatchAsync<object>($"fornecedores/pedidos/{pedidoId}/cancelar", new { empresaId });
    }
}
