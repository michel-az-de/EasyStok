using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class NotificacoesService(ApiClient api)
{
    public Task<ApiResult<List<Notificacao>>> ListarAsync(bool? lida = null, string? tipo = null, string? severidade = null)
    {
        var qs = "notificacoes?page=1&pageSize=200";
        if (lida.HasValue) qs += $"&lida={lida.Value.ToString().ToLower()}";
        if (!string.IsNullOrEmpty(tipo)) qs += $"&tipo={Uri.EscapeDataString(tipo)}";
        if (!string.IsNullOrEmpty(severidade)) qs += $"&severidade={Uri.EscapeDataString(severidade)}";
        return api.GetAsync<List<Notificacao>>(qs);
    }

    public Task<ApiResult<object>> BadgeAsync() =>
        api.GetAsync<object>("notificacoes/badge");

    public Task<ApiResult<NotificacaoResumo>> ResumoAsync() =>
        api.GetAsync<NotificacaoResumo>("notificacoes/resumo");

    public Task<ApiResult<List<Notificacao>>> RecentesAsync(int limit = 5) =>
        api.GetAsync<List<Notificacao>>($"notificacoes/recentes?limit={limit}");

    public Task<ApiResult<object>> MarcarLidaAsync(string id) =>
        api.PatchAsync<object>($"notificacoes/{id}/lida", new { });

    public Task<ApiResult<object>> MarcarTodasLidasAsync() =>
        api.PostAsync<object>("notificacoes/marcar-todas", new { });

    public Task<ApiResult<bool>> RemoverAsync(string id) =>
        api.DeleteAsync($"notificacoes/{id}");
}
