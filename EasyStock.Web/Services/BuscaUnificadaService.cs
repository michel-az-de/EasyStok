using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class BuscaUnificadaService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<ResultadoBuscaUnificada>>> BuscarAsync(string termo, int limite = 15) =>
        api.GetAsync<List<ResultadoBuscaUnificada>>(
            $"estoque/buscar?empresaId={GetEmpresaId()}&termo={Uri.EscapeDataString(termo)}&limite={limite}");
}
