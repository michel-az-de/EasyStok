using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class InteligenciaService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<InteligenciaBoardApi>> BoardAsync(int periodo = 30) =>
        api.GetAsync<InteligenciaBoardApi>($"inteligencia/board?empresaId={GetEmpresaId()}&periodo={periodo}");

    public Task<ApiResult<List<ItemEstoqueInteligenciaApi>>> EstoqueBaixoAsync(int pageSize = 10) =>
        api.GetAsync<List<ItemEstoqueInteligenciaApi>>($"inteligencia/estoque-baixo?empresaId={GetEmpresaId()}&pageSize={pageSize}");

    public Task<ApiResult<List<ItemEstoqueInteligenciaApi>>> ProximoVencimentoAsync(int dias = 30, int pageSize = 10) =>
        api.GetAsync<List<ItemEstoqueInteligenciaApi>>($"inteligencia/proximo-vencimento?empresaId={GetEmpresaId()}&dias={dias}&pageSize={pageSize}");

    public Task<ApiResult<List<ItemEstoqueInteligenciaApi>>> ItensParadosAsync(int dias = 60, int pageSize = 10) =>
        api.GetAsync<List<ItemEstoqueInteligenciaApi>>($"inteligencia/parados?empresaId={GetEmpresaId()}&dias={dias}&pageSize={pageSize}");

    public Task<ApiResult<List<ItemEstoqueInteligenciaApi>>> SugestoesReposicaoAsync(int pageSize = 10) =>
        api.GetAsync<List<ItemEstoqueInteligenciaApi>>($"inteligencia/sugestao-reposicao?empresaId={GetEmpresaId()}&pageSize={pageSize}");

    public Task<ApiResult<List<ProjecaoRupturaInteligenciaApi>>> ProjecaoRupturaAsync(int pageSize = 5) =>
        api.GetAsync<List<ProjecaoRupturaInteligenciaApi>>($"inteligencia/projecao-ruptura?empresaId={GetEmpresaId()}&pageSize={pageSize}");
}
