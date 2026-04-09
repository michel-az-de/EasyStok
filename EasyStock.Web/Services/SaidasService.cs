using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Saidas;

namespace EasyStock.Web.Services;

public class SaidasService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetLojaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<PagedResult<Movimentacao>>> ListarAsync(
        int page = 1, string? natureza = null, string? periodoInicio = null, string? periodoFim = null)
    {
        // The /movimentacoes endpoint only supports tipo, de and ate filters.
        // Natureza filtering is not available in the API; the parameter is retained
        // for API-level compatibility once/if the endpoint adds it.
        var qs = $"movimentacoes?page={page}&pageSize=20&tipo=Saida";
        if (!string.IsNullOrEmpty(periodoInicio)) qs += $"&de={Uri.EscapeDataString(periodoInicio)}";
        if (!string.IsNullOrEmpty(periodoFim)) qs += $"&ate={Uri.EscapeDataString(periodoFim)}";
        return api.GetAsync<PagedResult<Movimentacao>>(qs);
    }

    public async Task<ApiResult<object>> CriarAsync(SaidaFormViewModel vm)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty)
            return ApiResult<object>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");
        return await api.PostAsync<object>("estoque/saida", new
        {
            empresaId,
            itens = new[]
            {
                new
                {
                    produtoId = Guid.TryParse(vm.ProdutoId, out var pid) ? pid : Guid.Empty,
                    produtoVariacaoId = Guid.TryParse(vm.VarId, out var vid) ? vid : (Guid?)null,
                    quantidade = vm.Qty,
                    valorVendaUnitario = vm.Valor ?? 0m,
                    descricao = vm.Descricao
                }
            },
            dataVenda = vm.DtVenda.ToDateTime(TimeOnly.MinValue),
            dataSaida = (vm.DtSaida ?? vm.DtVenda).ToDateTime(TimeOnly.MinValue),
            dataEnvio = vm.DtEnvio.HasValue ? vm.DtEnvio.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
            notaFiscal = vm.NotaFiscal,
            natureza = MapNatureza(vm.Natureza),
            canal = MapCanal(vm.Canal),
            observacoes = vm.Descricao
        });
    }

    // EstornarAsync: no reversal endpoint exists in the API.
    // Returns a graceful failure so the controller shows an informative error toast.
    public Task<ApiResult<bool>> EstornarAsync(string id) =>
        Task.FromResult(ApiResult<bool>.Fail("NOT_SUPPORTED", "Estorno não disponível no momento."));

    // Maps lowercase UI natureza values to PascalCase API enum names.
    private static string MapNatureza(string? natureza) => natureza?.ToLowerInvariant() switch
    {
        "venda" => "Venda",
        "perda" => "Perda",
        "doacao" or "doação" => "Ajuste",   // no Doacao enum; Ajuste is the closest for a free exit
        "uso_interno" => "UsoInterno",
        "devolucao" or "devolução" => "Devolucao",
        "ajuste" => "Ajuste",
        "prejuizo" or "prejuízo" => "Prejuizo",
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
