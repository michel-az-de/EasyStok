using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Saidas;

namespace EasyStock.Web.Services;

public class SaidasService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<PagedResult<Movimentacao>>> ListarAsync(
        int page = 1, string? natureza = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = $"movimentacoes?empresaId={GetEmpresaId()}&page={page}&pageSize=20&tipo=Saida";
        if (!string.IsNullOrEmpty(natureza))
        {
            var mapped = MapNatureza(natureza);
            qs += $"&natureza={Uri.EscapeDataString(mapped)}";
        }
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Movimentacao>>(qs);
    }

    public Task<ApiResult<KpisResponse>> ObterKpisAsync(
        string? natureza = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = $"movimentacoes/kpis?empresaId={GetEmpresaId()}&tipo=Saida";
        if (!string.IsNullOrEmpty(natureza))
        {
            var mapped = MapNatureza(natureza);
            qs += $"&natureza={Uri.EscapeDataString(mapped)}";
        }
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<KpisResponse>(qs);
    }

    public async Task<ApiResult<object>> CriarAsync(SaidaFormViewModel vm)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return ApiResult<object>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");
        // ItemEstoqueId, quando presente, sinaliza saída de lote específico (sem FIFO).
        // Senão, ProdutoId aciona FIFO/FEFO no use case.
        Guid? itemEstoqueId = Guid.TryParse(vm.ItemEstoqueId, out var iid) ? iid : null;
        return await api.PostAsync<object>("estoque/saida", new
        {
            empresaId,
            itens = new[]
            {
                new
                {
                    itemEstoqueId,
                    produtoId = Guid.TryParse(vm.ProdutoId, out var pid) ? pid : Guid.Empty,
                    produtoVariacaoId = Guid.TryParse(vm.VarId, out var vid) ? vid : (Guid?)null,
                    quantidade = vm.Qty,
                    // Null quando não informado: permite que a API use o preço
                    // cadastrado do produto em vez de registrar ValorTotal = 0.
                    valorVendaUnitario = vm.Valor,
                    descricao = vm.Descricao
                }
            },
            dataVenda = DateTime.SpecifyKind(vm.DtVenda.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            dataSaida = DateTime.SpecifyKind((vm.DtSaida ?? vm.DtVenda).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            dataEnvio = vm.DtEnvio.HasValue
                ? DateTime.SpecifyKind(vm.DtEnvio.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : (DateTime?)null,
            notaFiscal = vm.NotaFiscal,
            natureza = MapNatureza(vm.Natureza),
            canal = MapCanal(vm.Canal),
            observacoes = vm.Descricao
        });
    }

    public Task<ApiResult<PagedResult<Movimentacao>>> ExportarAsync(string? periodoInicio = null, string? periodoFim = null)
    {
        var qs = $"movimentacoes?empresaId={GetEmpresaId()}&page=1&pageSize=1000&tipo=Saida";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Movimentacao>>(qs);
    }

    public Task<ApiResult<object>> EstornarAsync(string id, string motivo) =>
        api.PostAsync<object>($"estoque/estorno/{Uri.EscapeDataString(id)}?empresaId={GetEmpresaId()}", new { motivo });

    // Maps lowercase UI natureza values to PascalCase API enum names.
    private static string MapNatureza(string? natureza) => natureza?.ToLowerInvariant() switch
    {
        "venda" => "Venda",
        "perda" => "Perda",
        "doacao" or "doação" => "Doacao",
        "uso_interno" => "UsoInterno",
        "devolucao" or "devolução" => "Devolucao",
        "ajuste" => "Ajuste",
        "prejuizo" or "prejuízo" => "Prejuizo",
        "estorno" => "Estorno",
        _ => "Venda"
    };

    // Maps free-text canal values to CanalVenda enum names.
    private static string MapCanal(string? canal)
    {
        var c = canal?.ToLowerInvariant() ?? string.Empty;
        if (c.Contains("ml") || c.Contains("mercadolivre") || c.Contains("mercado livre"))
            return "MercadoLivre";
        if (c.Contains("shopee"))
            return "Shopee";
        if (c.Contains("whatsapp") || c.Contains("whats"))
            return "WhatsApp";
        if (c.Contains("instagram") || c.Contains("insta"))
            return "Instagram";
        if (c.Contains("loja") || c.Contains("proprio") || c.Contains("próprio"))
            return "LojaPropria";
        return "Outro";
    }
}
