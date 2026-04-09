using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Entradas;

namespace EasyStock.Web.Services;

public class EntradasService(ApiClient api)
{
    public Task<ApiResult<PagedResult<Entrada>>> HistoricoAsync(
        int page = 1, string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = $"movimentacoes?page={page}&pageSize=20";
        if (!string.IsNullOrEmpty(tipo)) qs += $"&tipo={Uri.EscapeDataString(tipo)}";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Entrada>>(qs);
    }

    public Task<ApiResult<object>> CriarEntradaAsync(EntradaFormViewModel vm) =>
        api.PostAsync<object>("estoque/entrada", new
        {
            produtoId = vm.ProdutoId,
            varId = vm.VarId,
            qty = vm.Qty,
            custo = vm.Custo,
            preco = vm.Preco,
            fornecedorId = vm.FornecedorId,
            lote = vm.Lote,
            validade = vm.Validade,
            observacoes = vm.Observacoes,
            data = vm.Data
        });

    public Task<ApiResult<object>> ReposicaoAsync(EntradaFormViewModel vm) =>
        api.PostAsync<object>("estoque/reposicao", new
        {
            produtoId = vm.ProdutoId,
            varId = vm.VarId,
            qty = vm.Qty,
            custo = vm.Custo,
            preco = vm.Preco,
            fornecedorId = vm.FornecedorId,
            lote = vm.Lote,
            validade = vm.Validade,
            observacoes = vm.Observacoes,
            data = vm.Data
        });
}
