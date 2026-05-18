using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.ConfiguracaoFiscal;

namespace EasyStock.Web.Services;

/// <summary>
/// Proxy do EasyStock.Web para os endpoints REST de configuracao fiscal
/// (controllers em <c>EasyStock.Api/Controllers/ConfiguracaoFiscalController.cs</c>).
/// Tenant resolvido via <see cref="SessionService.GetEmpresaId"/> e enviado
/// como <c>?empresaId=...</c>.
/// </summary>
public class ConfiguracaoFiscalService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private string Q(string path) => $"{path}?empresaId={GetEmpresaId()}";

    public Task<ApiResult<ConfiguracaoFiscalViewModel>> ObterAsync() =>
        api.GetAsync<ConfiguracaoFiscalViewModel>(Q("configuracao-fiscal"));

    public Task<ApiResult<object>> AtualizarDadosEmitenteAsync(
        string regimeTributario,
        string? inscricaoEstadual,
        string? inscricaoMunicipal,
        EnderecoFiscalDto? endereco) =>
        api.PostAsync<object>(Q("configuracao-fiscal/dados-emitente"), new
        {
            regimeTributario,
            inscricaoEstadual,
            inscricaoMunicipal,
            endereco,
        });

    public Task<ApiResult<object>> EscolherProvedorAsync(string provedor) =>
        api.PostAsync<object>(Q("configuracao-fiscal/provedor"), new { provedor });

    public Task<ApiResult<object>> ConfigurarCscAsync(string cscId, string cscToken) =>
        api.PostAsync<object>(Q("configuracao-fiscal/csc"), new { cscId, cscToken });

    public Task<ApiResult<object>> AlterarSerieAmbienteAsync(string? ambiente, short? serieNfce) =>
        api.PostAsync<object>(Q("configuracao-fiscal/serie-ambiente"), new { ambiente, serieNfce });

    public Task<ApiResult<object>> HabilitarAsync() =>
        api.PostAsync<object>(Q("configuracao-fiscal/habilitar"), new { });

    public Task<ApiResult<object>> DesabilitarAsync() =>
        api.PostAsync<object>(Q("configuracao-fiscal/desabilitar"), new { });
}
