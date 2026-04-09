using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class AnalyticsService(ApiClient api)
{
    public Task<ApiResult<object>> DashboardAsync() =>
        api.GetAsync<object>("analytics/dashboard");

    public Task<ApiResult<object>> ProjecoesAsync() =>
        api.GetAsync<object>("analytics/projecoes");

    public Task<ApiResult<List<EstoqueSku>>> ReposicaoAsync() =>
        api.GetAsync<List<EstoqueSku>>("analytics/reposicao");

    public Task<ApiResult<object>> SazonalidadeAsync() =>
        api.GetAsync<object>("analytics/sazonalidade");

    public Task<ApiResult<object>> AlertasAsync() =>
        api.GetAsync<object>("analytics/alertas");

    public Task<ApiResult<PagedResult<object>>> MovimentacoesAsync(
        int page = 1, string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = $"movimentacoes?page={page}&pageSize=20";
        if (!string.IsNullOrEmpty(tipo)) qs += $"&tipo={Uri.EscapeDataString(tipo)}";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<object>>(qs);
    }
}
