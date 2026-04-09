using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class AnalyticsService(ApiClient api)
{
    public Task<ApiResult<object>> DashboardAsync() =>
        api.GetAsync<object>("analytics/dashboard");

    public Task<ApiResult<List<ReposicaoSugerida>>> ReposicaoAsync() =>
        api.GetAsync<List<ReposicaoSugerida>>("analytics/reposicao");

    public Task<ApiResult<List<ValidadeAlerta>>> AlertasAsync() =>
        api.GetAsync<List<ValidadeAlerta>>("analytics/alertas");

    public Task<ApiResult<List<MovimentacaoResumo>>> MovimentacoesAsync(
        string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = "analytics/movimentacoes?diasPadrao=30";
        if (!string.IsNullOrEmpty(tipo)) qs += $"&tipo={Uri.EscapeDataString(tipo)}";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<List<MovimentacaoResumo>>(qs);
    }
}
