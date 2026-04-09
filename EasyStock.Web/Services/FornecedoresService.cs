using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class FornecedoresService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<Fornecedor>>> ListarAsync(string? status = null, string? search = null)
    {
        var qs = $"fornecedores?empresaId={GetEmpresaId()}";
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
}
