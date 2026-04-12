using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class NotificacoesService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<Notificacao>>> ListarAsync(bool? lida = null, string? tipo = null, string? severidade = null)
    {
        var qs = $"notificacoes?empresaId={GetEmpresaId()}&page=1&pageSize=200";
        if (lida.HasValue) qs += $"&lida={lida.Value.ToString().ToLower()}";
        if (!string.IsNullOrEmpty(tipo)) qs += $"&tipo={Uri.EscapeDataString(tipo)}";
        if (!string.IsNullOrEmpty(severidade)) qs += $"&severidade={Uri.EscapeDataString(severidade)}";
        return api.GetAsync<List<Notificacao>>(qs);
    }

    public Task<ApiResult<object>> BadgeAsync() =>
        api.GetAsync<object>($"notificacoes/badge?empresaId={GetEmpresaId()}");

    public Task<ApiResult<NotificacaoResumo>> ResumoAsync() =>
        api.GetAsync<NotificacaoResumo>($"notificacoes/resumo?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<Notificacao>>> RecentesAsync(int limit = 5) =>
        api.GetAsync<List<Notificacao>>($"notificacoes/recentes?empresaId={GetEmpresaId()}&limit={limit}");

    public Task<ApiResult<object>> MarcarLidaAsync(string id) =>
        api.PatchAsync<object>($"notificacoes/{id}/lida?empresaId={GetEmpresaId()}", new { });

    public Task<ApiResult<object>> MarcarTodasLidasAsync() =>
        api.PostAsync<object>($"notificacoes/marcar-todas?empresaId={GetEmpresaId()}", new { });

    public Task<ApiResult<bool>> RemoverAsync(string id) =>
        api.DeleteAsync($"notificacoes/{id}?empresaId={GetEmpresaId()}");
}
