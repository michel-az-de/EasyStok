using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class EtiquetasService(ApiClient api, SessionService session)
{
    private string GetEmpresaId() => session.GetEmpresaId() ?? "";

    public Task<ApiResult<List<EtiquetaTemplateApi>>> ListarAsync() =>
        api.GetAsync<List<EtiquetaTemplateApi>>($"etiquetas/templates?empresaId={GetEmpresaId()}");

    public Task<ApiResult<EtiquetaTemplateApi>> ObterAsync(string origem, string id) =>
        api.GetAsync<EtiquetaTemplateApi>($"etiquetas/templates/{origem}/{id}?empresaId={GetEmpresaId()}");
}
