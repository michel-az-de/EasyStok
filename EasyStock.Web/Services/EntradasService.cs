using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Entradas;

namespace EasyStock.Web.Services;

public class EntradasService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<PagedResult<Movimentacao>>> HistoricoAsync(
        int page = 1, string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var tipoApi = tipo?.ToLowerInvariant() switch
        {
            "saida" or "saída" => "Saida",
            "reposicao" or "reposição" => "Entrada",
            _ => "Entrada"
        };

        var qs = $"movimentacoes?empresaId={GetEmpresaId()}&page={page}&pageSize=20&tipo={Uri.EscapeDataString(tipoApi)}";
        if (tipo?.ToLowerInvariant() is "reposicao" or "reposição")
            qs += "&natureza=Reposicao";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Movimentacao>>(qs);
    }

    public async Task<ApiResult<object>> CriarEntradaAsync(EntradaFormViewModel vm)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return ApiResult<object>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");
        return await api.PostAsync<object>("estoque/entrada", BuildEntradaBody(vm, "Compra", empresaId));
    }

    public async Task<ApiResult<object>> ReposicaoAsync(ReposicaoFormViewModel vm)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return ApiResult<object>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");

        return await api.PostAsync<object>("estoque/reposicao", new
        {
            empresaId,
            itemEstoqueId = Guid.TryParse(vm.ItemEstoqueId, out var iid) ? iid : Guid.Empty,
            quantidadeAdicional = vm.Qty,
            novoCustoUnitario = vm.Custo,
            novoPrecoVendaSugerido = vm.Preco,
            dataReposicao = DateTime.SpecifyKind(vm.Data.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            observacoes = vm.Observacoes,
            novaValidade = vm.Validade.HasValue
                ? DateTime.SpecifyKind(vm.Validade.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : (DateTime?)null
        });
    }

    public Task<ApiResult<PagedResult<Movimentacao>>> ExportarAsync(string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var tipoFilter = string.IsNullOrEmpty(tipo) ? "Entrada" : tipo;
        var qs = $"movimentacoes?empresaId={GetEmpresaId()}&page=1&pageSize=1000&tipo={Uri.EscapeDataString(tipoFilter)}";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Movimentacao>>(qs);
    }

    private static object BuildEntradaBody(EntradaFormViewModel vm, string natureza, Guid empresaId) => new
    {
        empresaId,
        produtoId = Guid.TryParse(vm.ProdutoId, out var pid) ? pid : Guid.Empty,
        produtoVariacaoId = Guid.TryParse(vm.VarId, out var vid) ? vid : (Guid?)null,
        quantidade = vm.Qty,
        custoUnitario = vm.Custo,
        precoVendaSugerido = vm.Preco,
        dataEntrada = DateTime.SpecifyKind(vm.Data.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
        natureza,
        codigoLote = vm.Lote,
        validade = vm.Validade.HasValue
            ? DateTime.SpecifyKind(vm.Validade.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
            : (DateTime?)null,
        observacoes = vm.Observacoes
    };
}
