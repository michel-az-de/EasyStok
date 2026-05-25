using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class AnalyticsService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<DashboardResumoApi>> DashboardAsync() =>
        api.GetAsync<DashboardResumoApi>($"analytics/dashboard?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<ReposicaoSugerida>>> ReposicaoAsync() =>
        api.GetAsync<List<ReposicaoSugerida>>($"analytics/reposicao?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<ValidadeAlerta>>> AlertasAsync() =>
        api.GetAsync<List<ValidadeAlerta>>($"analytics/alertas?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<ReceitaPorPeriodoApi>>> ReceitaAsync(int meses = 6) =>
        api.GetAsync<List<ReceitaPorPeriodoApi>>($"analytics/receita?empresaId={GetEmpresaId()}&meses={meses}");

    public Task<ApiResult<List<MovimentacaoResumo>>> MovimentacoesAsync(
        string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = $"analytics/movimentacoes?empresaId={GetEmpresaId()}&diasPadrao=30";
        if (!string.IsNullOrEmpty(tipo)) qs += $"&tipo={Uri.EscapeDataString(tipo)}";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<List<MovimentacaoResumo>>(qs);
    }
}
