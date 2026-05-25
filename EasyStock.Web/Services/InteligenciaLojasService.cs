using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class InteligenciaLojasService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<LojaComparacaoApi>>> ComparacaoAsync(int periodo = 30) =>
        api.GetAsync<List<LojaComparacaoApi>>($"inteligencia-lojas/comparacao?empresaId={GetEmpresaId()}&periodo={periodo}");

    public Task<ApiResult<LojaResumoInteligenciaApi>> ResumoLojaAsync(Guid lojaId, int periodo = 30) =>
        api.GetAsync<LojaResumoInteligenciaApi>($"inteligencia-lojas/{lojaId}?empresaId={GetEmpresaId()}&periodo={periodo}");

    public Task<ApiResult<List<ProdutoTurnoverApi>>> TopProdutosAsync(Guid lojaId, int periodo = 30, int top = 10) =>
        api.GetAsync<List<ProdutoTurnoverApi>>($"inteligencia-lojas/{lojaId}/top-produtos?empresaId={GetEmpresaId()}&periodo={periodo}&top={top}");

    public Task<ApiResult<List<ProdutoTurnoverApi>>> BottomProdutosAsync(Guid lojaId, int periodo = 30, int top = 10) =>
        api.GetAsync<List<ProdutoTurnoverApi>>($"inteligencia-lojas/{lojaId}/bottom-produtos?empresaId={GetEmpresaId()}&periodo={periodo}&top={top}");

    public Task<ApiResult<List<ValidadeAlerta>>> AlertasValidadeAsync(Guid lojaId, int dias = 30) =>
        api.GetAsync<List<ValidadeAlerta>>($"inteligencia-lojas/{lojaId}/validade?empresaId={GetEmpresaId()}&dias={dias}");

    public Task<ApiResult<List<ReposicaoSugerida>>> ReposicoesAsync(Guid lojaId, int diasHistorico = 30) =>
        api.GetAsync<List<ReposicaoSugerida>>($"inteligencia-lojas/{lojaId}/reposicao?empresaId={GetEmpresaId()}&diasHistorico={diasHistorico}");

    public Task<ApiResult<List<IndicadorAcaoApi>>> IndicadoresAsync(int periodo = 30, Guid? lojaId = null)
    {
        var qs = $"inteligencia-lojas/indicadores?empresaId={GetEmpresaId()}&periodo={periodo}";
        if (lojaId.HasValue) qs += $"&lojaId={lojaId.Value}";
        return api.GetAsync<List<IndicadorAcaoApi>>(qs);
    }
}
