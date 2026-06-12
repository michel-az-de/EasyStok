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
        var qs = $"movimentacoes?empresaId={GetEmpresaId()}&page={page}&pageSize=20&tipo=Entrada";

        var tipoNorm = tipo?.ToLowerInvariant();
        if (tipoNorm is "reposicao" or "reposição")
            qs += "&natureza=Reposicao";
        else if (tipoNorm is "compra" or "entrada")
            qs += "&natureza=Compra";
        // null = sem filtro de natureza (mostra Compra + Reposicao)

        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Movimentacao>>(qs);
    }

    public async Task<ApiResult<EntradaCriadaApi>> CriarEntradaAsync(EntradaFormViewModel vm)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return ApiResult<EntradaCriadaApi>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");
        return await api.PostAsync<EntradaCriadaApi>("estoque/entrada", BuildEntradaBody(vm, "Compra", empresaId));
    }

    /// <summary>Busca lotes existentes da empresa (combobox da entrada). Filtra por codigo/operador/obs.</summary>
    public async Task<ApiResult<PagedResult<LoteBuscaApi>>> BuscarLotesAsync(string? termo)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return ApiResult<PagedResult<LoteBuscaApi>>.Fail("EMPRESA_INVALIDA", "Loja não identificada.");
        var qs = $"lotes?empresaId={empresaId}&pageSize=8";
        if (!string.IsNullOrWhiteSpace(termo)) qs += $"&search={Uri.EscapeDataString(termo)}";
        return await api.GetAsync<PagedResult<LoteBuscaApi>>(qs);
    }

    /// <summary>Gera o proximo codigo de lote para uma entrada do produto (LOTE-SKU-AAMMDD-NNN).</summary>
    public async Task<ApiResult<ProximoCodigoLoteApi>> ProximoCodigoLoteAsync(Guid produtoId)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return ApiResult<ProximoCodigoLoteApi>.Fail("EMPRESA_INVALIDA", "Loja não identificada.");
        return await api.GetAsync<ProximoCodigoLoteApi>(
            $"lotes/proximo-codigo-entrada?empresaId={empresaId}&produtoId={produtoId}");
    }

    /// <summary>Proxy do PDF (etiqueta/nota) da entrada — streama o binario vindo da Api.</summary>
    public Task<ApiResult<Stream>> DocumentoPdfAsync(string tipo, Guid itemEstoqueId)
    {
        var empresaId = GetEmpresaId();
        var rota = tipo == "etiqueta" ? "etiqueta/pdf" : "nota-entrada/pdf";
        return api.GetStreamAsync($"estoque/{itemEstoqueId}/{rota}?empresaId={empresaId}");
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
