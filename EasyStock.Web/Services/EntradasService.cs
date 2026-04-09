using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Entradas;

namespace EasyStock.Web.Services;

public class EntradasService(ApiClient api)
{
    public Task<ApiResult<PagedResult<Movimentacao>>> HistoricoAsync(
        int page = 1, string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        // Map UI tipo filters to TipoMovimentacaoEstoque enum values recognised by the API.
        // When no filter is selected we default to Entrada so the Entradas history
        // only shows entry movements.
        var tipoApi = tipo?.ToLowerInvariant() switch
        {
            "saida" or "saída" => "Saida",
            "reposicao" or "reposição" => "Entrada",  // reposições are stored as Entrada movements
            _ => "Entrada"
        };

        var qs = $"movimentacoes?page={page}&pageSize=20&tipo={Uri.EscapeDataString(tipoApi)}";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Movimentacao>>(qs);
    }

    public Task<ApiResult<object>> CriarEntradaAsync(EntradaFormViewModel vm) =>
        api.PostAsync<object>("estoque/entrada", BuildEntradaBody(vm, "Compra"));

    // Reposição rápida: the UI selects by ProdutoId; the API's reposicao endpoint requires an
    // existing ItemEstoqueId. We instead call the entrada endpoint with Natureza=Reposicao so
    // the contract is satisfied without requiring ItemEstoqueId on the form.
    public Task<ApiResult<object>> ReposicaoAsync(EntradaFormViewModel vm) =>
        api.PostAsync<object>("estoque/entrada", BuildEntradaBody(vm, "Reposicao"));

    private static object BuildEntradaBody(EntradaFormViewModel vm, string natureza) => new
    {
        produtoId = Guid.TryParse(vm.ProdutoId, out var pid) ? pid : Guid.Empty,
        produtoVariacaoId = Guid.TryParse(vm.VarId, out var vid) ? vid : (Guid?)null,
        quantidade = vm.Qty,
        custoUnitario = vm.Custo,
        precoVendaSugerido = vm.Preco,
        dataEntrada = vm.Data.ToDateTime(TimeOnly.MinValue),
        natureza,
        codigoLote = vm.Lote,
        validade = vm.Validade.HasValue ? vm.Validade.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
        observacoes = vm.Observacoes
    };
}
