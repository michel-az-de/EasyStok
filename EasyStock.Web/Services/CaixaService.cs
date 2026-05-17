using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>Onda P3 — UI Web do módulo Caixa (movimentos + fechamento).</summary>
public class CaixaService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private static ApiResult<T> EmpresaErr<T>() =>
        ApiResult<T>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");

    public Task<ApiResult<CaixaDia>> ObterDiaAsync(DateOnly? data = null) =>
        api.GetAsync<CaixaDia>($"caixa/dia?empresaId={GetEmpresaId()}{(data.HasValue ? $"&data={data:yyyy-MM-dd}" : "")}");

    public Task<ApiResult<MovimentoCaixa>> AbrirAsync(decimal saldoInicial, string? observacoes)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<MovimentoCaixa>());
        return api.PostAsync<MovimentoCaixa>("caixa/abrir", new { empresaId, saldoInicial, observacoes, origem = "web" });
    }

    public Task<ApiResult<FechamentoCaixa>> FecharAsync(DateOnly? data = null, string? observacoes = null)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<FechamentoCaixa>());
        return api.PostAsync<FechamentoCaixa>("caixa/fechar", new { empresaId, data, observacoes });
    }

    public Task<ApiResult<MovimentoCaixa>> RegistrarMovimentoAsync(
        string tipo, decimal valor, string? descricao,
        string? metodo, string? categoria, string? referencia, DateTime? dataMovimento)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<MovimentoCaixa>());
        return api.PostAsync<MovimentoCaixa>("caixa/movimentos", new
        {
            empresaId, tipo, valor, descricao, metodo, categoria, referencia,
            dataMovimento, origem = "web"
        });
    }

    public Task<ApiResult<MovimentoCaixa>> EstornarAsync(string id, string? motivo)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<MovimentoCaixa>());
        return api.PostAsync<MovimentoCaixa>($"caixa/movimentos/{id}/estornar", new
        {
            empresaId, id = Guid.Parse(id), motivo
        });
    }

    public Task<ApiResult<List<FechamentoCaixa>>> ListarFechamentosAsync() =>
        api.GetAsync<List<FechamentoCaixa>>($"caixa/fechamentos?empresaId={GetEmpresaId()}&page=1&pageSize=500");

    public Task<ApiResult<List<MobileCashSummary>>> ListarMobileAsync(bool pendingOnly = false)
    {
        var qs = $"mobile/cash?empresaId={GetEmpresaId()}";
        if (pendingOnly) qs += "&pendingOnly=true";
        return api.GetAsync<List<MobileCashSummary>>(qs);
    }

    public Task<ApiResult<object>> PromoverMobileAsync(string mobileId) =>
        api.PostAsync<object>($"mobile/cash/{mobileId}/promover", new { });

    public Task<ApiResult<object>> UnlinkMobileAsync(string mobileId) =>
        api.PostAsync<object>($"mobile/cash/{mobileId}/unlink", new { });
}
