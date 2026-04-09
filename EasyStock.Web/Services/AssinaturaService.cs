using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class AssinaturaService(ApiClient api)
{
    public Task<ApiResult<Assinatura>> ObterAsync() =>
        api.GetAsync<Assinatura>("assinatura");

    public Task<ApiResult<object>> PlanosAsync() =>
        api.GetAsync<object>("assinatura/planos");

    public Task<ApiResult<object>> UsoAsync() =>
        api.GetAsync<object>("assinatura/uso");

    public Task<ApiResult<object>> FaturasAsync() =>
        api.GetAsync<object>("assinatura/faturas");
}
