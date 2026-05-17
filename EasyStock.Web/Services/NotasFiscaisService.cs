using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class NotasFiscaisService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<PagedResult<NfeListItem>>> ListarAsync(
        int page = 1, string? status = null, string? desde = null, string? ate = null, string? search = null)
    {
        var qs = $"notas-fiscais?empresaId={GetEmpresaId()}&page={page}&pageSize=20";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(desde)) qs += $"&desde={Uri.EscapeDataString(desde)}";
        if (!string.IsNullOrEmpty(ate)) qs += $"&ate={Uri.EscapeDataString(ate)}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync<PagedResult<NfeListItem>>(qs);
    }

    public Task<ApiResult<NfeDetalhe>> ObterAsync(Guid id)
    {
        return api.GetAsync<NfeDetalhe>($"notas-fiscais/{id}?empresaId={GetEmpresaId()}");
    }

    public Task<ApiResult<object>> CancelarAsync(Guid id, string motivo)
    {
        return api.PostAsync<object>($"notas-fiscais/{id}/cancelar?empresaId={GetEmpresaId()}", new { motivo });
    }
}
