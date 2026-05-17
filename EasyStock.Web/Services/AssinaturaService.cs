using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class AssinaturaService(ApiClient api)
{
    public Task<ApiResult<List<PlanoDto>>> ListarPlanosAsync() =>
        api.GetAsync<List<PlanoDto>>("planos");

    public Task<ApiResult<object>> GetStatusAsync() =>
        api.GetAsync<object>("assinatura");
}
