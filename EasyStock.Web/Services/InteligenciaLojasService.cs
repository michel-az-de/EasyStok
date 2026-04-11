using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class InteligenciaLojasService(ApiClient api)
{
    public Task<ApiResult<List<LojaComparacaoApi>>> ComparacaoAsync(int periodo = 30) =>
        api.GetAsync<List<LojaComparacaoApi>>($"inteligencia-lojas/comparacao?periodo={periodo}");

    public Task<ApiResult<LojaResumoInteligenciaApi>> ResumoLojaAsync(Guid lojaId, int periodo = 30) =>
        api.GetAsync<LojaResumoInteligenciaApi>($"inteligencia-lojas/{lojaId}?periodo={periodo}");

    public Task<ApiResult<List<ProdutoTurnoverApi>>> TopProdutosAsync(Guid lojaId, int periodo = 30, int top = 10) =>
        api.GetAsync<List<ProdutoTurnoverApi>>($"inteligencia-lojas/{lojaId}/top-produtos?periodo={periodo}&top={top}");

    public Task<ApiResult<List<ProdutoTurnoverApi>>> BottomProdutosAsync(Guid lojaId, int periodo = 30, int top = 10) =>
        api.GetAsync<List<ProdutoTurnoverApi>>($"inteligencia-lojas/{lojaId}/bottom-produtos?periodo={periodo}&top={top}");

    public Task<ApiResult<List<ValidadeAlerta>>> AlertasValidadeAsync(Guid lojaId, int dias = 30) =>
        api.GetAsync<List<ValidadeAlerta>>($"inteligencia-lojas/{lojaId}/validade?dias={dias}");

    public Task<ApiResult<List<ReposicaoSugerida>>> ReposicoesAsync(Guid lojaId, int diasHistorico = 30) =>
        api.GetAsync<List<ReposicaoSugerida>>($"inteligencia-lojas/{lojaId}/reposicao?diasHistorico={diasHistorico}");

    public Task<ApiResult<List<IndicadorAcaoApi>>> IndicadoresAsync(int periodo = 30, Guid? lojaId = null)
    {
        var qs = $"inteligencia-lojas/indicadores?periodo={periodo}";
        if (lojaId.HasValue) qs += $"&lojaId={lojaId.Value}";
        return api.GetAsync<List<IndicadorAcaoApi>>(qs);
    }
}
