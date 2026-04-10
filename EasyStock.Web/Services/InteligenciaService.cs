using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class InteligenciaService(ApiClient api)
{
    public Task<ApiResult<InteligenciaBoardApi>> BoardAsync(int periodo = 30) =>
        api.GetAsync<InteligenciaBoardApi>($"inteligencia/board?periodo={periodo}");

    public Task<ApiResult<List<ItemEstoqueInteligenciaApi>>> EstoqueBaixoAsync(int pageSize = 10) =>
        api.GetAsync<List<ItemEstoqueInteligenciaApi>>($"inteligencia/estoque-baixo?pageSize={pageSize}");

    public Task<ApiResult<List<ItemEstoqueInteligenciaApi>>> ProximoVencimentoAsync(int dias = 30, int pageSize = 10) =>
        api.GetAsync<List<ItemEstoqueInteligenciaApi>>($"inteligencia/proximo-vencimento?dias={dias}&pageSize={pageSize}");

    public Task<ApiResult<List<ItemEstoqueInteligenciaApi>>> ItensParadosAsync(int dias = 60, int pageSize = 10) =>
        api.GetAsync<List<ItemEstoqueInteligenciaApi>>($"inteligencia/parados?dias={dias}&pageSize={pageSize}");

    public Task<ApiResult<List<ItemEstoqueInteligenciaApi>>> SugestoesReposicaoAsync(int pageSize = 10) =>
        api.GetAsync<List<ItemEstoqueInteligenciaApi>>($"inteligencia/sugestao-reposicao?pageSize={pageSize}");

    public Task<ApiResult<List<ProjecaoRupturaInteligenciaApi>>> ProjecaoRupturaAsync(int pageSize = 5) =>
        api.GetAsync<List<ProjecaoRupturaInteligenciaApi>>($"inteligencia/projecao-ruptura?pageSize={pageSize}");
}
