using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Saidas;

namespace EasyStock.Web.Services;

public class SaidasService(ApiClient api)
{
    public Task<ApiResult<PagedResult<Saida>>> ListarAsync(
        int page = 1, string? natureza = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = $"estoque/saida?page={page}&pageSize=20";
        if (!string.IsNullOrEmpty(natureza)) qs += $"&natureza={Uri.EscapeDataString(natureza)}";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Saida>>(qs);
    }

    public Task<ApiResult<object>> CriarAsync(SaidaFormViewModel vm) =>
        api.PostAsync<object>("estoque/saida", new
        {
            produtoId = vm.ProdutoId,
            varId = vm.VarId,
            natureza = vm.Natureza,
            qty = vm.Qty,
            valor = vm.Valor,
            dtVenda = vm.DtVenda,
            dtSaida = vm.DtSaida,
            dtEnvio = vm.DtEnvio,
            notaFiscal = vm.NotaFiscal,
            canal = vm.Canal,
            descricao = vm.Descricao
        });

    public Task<ApiResult<object>> ResumoAsync(string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = "estoque/saida/resumo";
        var sep = "?";
        if (!string.IsNullOrEmpty(periodoInicio)) { qs += $"{sep}de={Uri.EscapeDataString(periodoInicio)}"; sep = "&"; }
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"{sep}ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<object>(qs);
    }

    public Task<ApiResult<bool>> EstornarAsync(string id) =>
        api.DeleteAsync($"estoque/saida/{id}");
}
