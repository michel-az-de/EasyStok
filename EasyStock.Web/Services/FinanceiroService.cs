using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Financeiro;

namespace EasyStock.Web.Services;

/// <summary>
/// Service Web pra modulo Financeiro (Contas a Pagar/Receber, Categorias, Centros de Custo).
/// Consome a API REST com EmpresaId da sessao.
/// </summary>
public class FinanceiroService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private static ApiResult<T> EmpresaErr<T>() =>
        ApiResult<T>.Fail("EMPRESA_INVALIDA", "Empresa nao identificada. Selecione uma loja.");

    // ── Categorias ────────────────────────────────────────────────────────

    public Task<ApiResult<List<CategoriaFinanceiraApi>>> ListarCategoriasAsync(bool? ativa = null, string? tipo = null)
    {
        var qs = $"financeiro/categorias?empresaId={GetEmpresaId()}";
        if (ativa.HasValue) qs += $"&ativa={ativa.Value.ToString().ToLower()}";
        if (!string.IsNullOrWhiteSpace(tipo)) qs += $"&tipo={tipo}";
        return api.GetAsync<List<CategoriaFinanceiraApi>>(qs);
    }

    public Task<ApiResult<CategoriaFinanceiraApi>> CriarCategoriaAsync(string nome, string tipo, Guid? parentId, string? cor, string? icone)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<CategoriaFinanceiraApi>());
        return api.PostAsync<CategoriaFinanceiraApi>("financeiro/categorias",
            new { empresaId, nome, tipo, parentId, cor, icone, ordem = 0 });
    }

    public Task<ApiResult<object>> InativarCategoriaAsync(Guid id) =>
        api.PostAsync<object>($"financeiro/categorias/{id}/inativar?empresaId={GetEmpresaId()}", new { });

    // ── Centros de Custo ──────────────────────────────────────────────────

    public Task<ApiResult<List<CentroCustoApi>>> ListarCentrosCustoAsync(bool? ativo = null)
    {
        var qs = $"financeiro/centros-custo?empresaId={GetEmpresaId()}";
        if (ativo.HasValue) qs += $"&ativo={ativo.Value.ToString().ToLower()}";
        return api.GetAsync<List<CentroCustoApi>>(qs);
    }

    public Task<ApiResult<CentroCustoApi>> CriarCentroCustoAsync(string codigo, string nome, Guid? lojaId, string? descricao)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<CentroCustoApi>());
        return api.PostAsync<CentroCustoApi>("financeiro/centros-custo",
            new { empresaId, codigo, nome, lojaId, descricao });
    }

    public Task<ApiResult<object>> InativarCentroCustoAsync(Guid id) =>
        api.PostAsync<object>($"financeiro/centros-custo/{id}/inativar?empresaId={GetEmpresaId()}", new { });

    // ── Contas a Pagar ────────────────────────────────────────────────────

    public Task<ApiResult<ContasPagarPaginadas>> ListarContasPagarAsync(
        string? status = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        string? busca = null,
        int page = 1,
        int pageSize = 20)
    {
        var qs = $"contas-a-pagar?empresaId={GetEmpresaId()}&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(status)) qs += $"&status={status}";
        if (vencimentoDe.HasValue) qs += $"&vencimentoDe={vencimentoDe.Value:yyyy-MM-dd}";
        if (vencimentoAte.HasValue) qs += $"&vencimentoAte={vencimentoAte.Value:yyyy-MM-dd}";
        if (!string.IsNullOrWhiteSpace(busca)) qs += $"&busca={Uri.EscapeDataString(busca)}";
        return api.GetAsync<ContasPagarPaginadas>(qs);
    }

    public Task<ApiResult<ContaPagarApi>> ObterContaPagarAsync(Guid id) =>
        api.GetAsync<ContaPagarApi>($"contas-a-pagar/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<ContaPagarApi>> CriarContaPagarAsync(
        string descricao,
        Guid categoriaFinanceiraId,
        DateTime dataEmissao,
        IEnumerable<object> parcelas,
        Guid? fornecedorId = null,
        Guid? centroCustoId = null,
        string? observacoes = null,
        bool emitirAposCriar = false)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<ContaPagarApi>());
        return api.PostAsync<ContaPagarApi>("contas-a-pagar", new
        {
            empresaId,
            fornecedorId,
            categoriaFinanceiraId,
            centroCustoId,
            descricao,
            dataEmissao,
            parcelas,
            observacoes,
            origem = "Manual",
            emitirAposCriar
        });
    }

    public Task<ApiResult<ContaPagarApi>> EmitirContaPagarAsync(Guid id) =>
        api.PostAsync<ContaPagarApi>($"contas-a-pagar/{id}/emitir?empresaId={GetEmpresaId()}", new { });

    public Task<ApiResult<ContaPagarApi>> CancelarContaPagarAsync(Guid id, string motivo) =>
        api.PostAsync<ContaPagarApi>($"contas-a-pagar/{id}/cancelar", new { empresaId = GetEmpresaId(), motivo });

    // ── Contas a Receber ──────────────────────────────────────────────────

    public Task<ApiResult<ContasReceberPaginadas>> ListarContasReceberAsync(
        string? status = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        string? busca = null,
        int page = 1,
        int pageSize = 20)
    {
        var qs = $"contas-a-receber?empresaId={GetEmpresaId()}&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(status)) qs += $"&status={status}";
        if (vencimentoDe.HasValue) qs += $"&vencimentoDe={vencimentoDe.Value:yyyy-MM-dd}";
        if (vencimentoAte.HasValue) qs += $"&vencimentoAte={vencimentoAte.Value:yyyy-MM-dd}";
        if (!string.IsNullOrWhiteSpace(busca)) qs += $"&busca={Uri.EscapeDataString(busca)}";
        return api.GetAsync<ContasReceberPaginadas>(qs);
    }

    public Task<ApiResult<ContaReceberApi>> ObterContaReceberAsync(Guid id) =>
        api.GetAsync<ContaReceberApi>($"contas-a-receber/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<ContaReceberApi>> CriarContaReceberAsync(
        string descricao,
        Guid categoriaFinanceiraId,
        DateTime dataEmissao,
        IEnumerable<object> parcelas,
        Guid? clienteId = null,
        Guid? centroCustoId = null,
        string? observacoes = null,
        bool emitirAposCriar = false)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<ContaReceberApi>());
        return api.PostAsync<ContaReceberApi>("contas-a-receber", new
        {
            empresaId,
            clienteId,
            categoriaFinanceiraId,
            centroCustoId,
            descricao,
            dataEmissao,
            parcelas,
            observacoes,
            origem = "Manual",
            emitirAposCriar
        });
    }

    public Task<ApiResult<ContaReceberApi>> EmitirContaReceberAsync(Guid id) =>
        api.PostAsync<ContaReceberApi>($"contas-a-receber/{id}/emitir?empresaId={GetEmpresaId()}", new { });

    public Task<ApiResult<ContaReceberApi>> CancelarContaReceberAsync(Guid id, string motivo) =>
        api.PostAsync<ContaReceberApi>($"contas-a-receber/{id}/cancelar", new { empresaId = GetEmpresaId(), motivo });

    // ── Pagamentos ────────────────────────────────────────────────────────

    public Task<ApiResult<object>> RegistrarPagamentoCpAsync(Guid contaId, Guid parcelaId, decimal valor, string metodo, string? observacao)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<object>());
        return api.PostAsync<object>($"contas-a-pagar/{contaId}/parcelas/{parcelaId}/pagamentos",
            new { empresaId, valor, metodo, observacao, gatewayProvedor = "Manual" });
    }

    public Task<ApiResult<object>> RegistrarPagamentoCrAsync(Guid contaId, Guid parcelaId, decimal valor, string metodo, string? observacao)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<object>());
        return api.PostAsync<object>($"contas-a-receber/{contaId}/parcelas/{parcelaId}/pagamentos",
            new { empresaId, valor, metodo, observacao, gatewayProvedor = "Manual" });
    }

    public Task<ApiResult<object>> EstornarPagamentoCpAsync(Guid parcelaId, Guid pagId, string? motivo)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<object>());
        return api.PostAsync<object>($"contas-a-pagar/parcelas/{parcelaId}/pagamentos/{pagId}/estornar",
            new { empresaId, motivo });
    }

    public Task<ApiResult<object>> EstornarPagamentoCrAsync(Guid parcelaId, Guid pagId, string? motivo)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<object>());
        return api.PostAsync<object>($"contas-a-receber/parcelas/{parcelaId}/pagamentos/{pagId}/estornar",
            new { empresaId, motivo });
    }

    // ── Pix QR ────────────────────────────────────────────────────────────

    public Task<ApiResult<PixQrResultApi>> GerarPixAsync(Guid parcelaId)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<PixQrResultApi>());
        return api.PostAsync<PixQrResultApi>($"contas-a-receber/parcelas/{parcelaId}/pix",
            new { empresaId });
    }

    // ── Dashboard ─────────────────────────────────────────────────────────

    public Task<ApiResult<DashboardFinanceiroApi>> ObterDashboardAsync() =>
        api.GetAsync<DashboardFinanceiroApi>($"financeiro/dashboard?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<FluxoBucketApi>>> ObterFluxoCaixaAsync(
        string periodicidade = "Mensal", DateTime? inicio = null, DateTime? fim = null)
    {
        var qs = $"financeiro/fluxo-caixa?empresaId={GetEmpresaId()}&periodicidade={periodicidade}";
        if (inicio.HasValue) qs += $"&inicio={inicio.Value:yyyy-MM-dd}";
        if (fim.HasValue) qs += $"&fim={fim.Value:yyyy-MM-dd}";
        return api.GetAsync<List<FluxoBucketApi>>(qs);
    }
}

public class FluxoBucketApi
{
    public DateTime InicioBucket { get; set; }
    public DateTime FimBucket { get; set; }
    public string Rotulo { get; set; } = "";
    public decimal PrevistoPagar { get; set; }
    public decimal PrevistoReceber { get; set; }
    public decimal RealizadoPagar { get; set; }
    public decimal RealizadoReceber { get; set; }
}

public class PixQrResultApi
{
    public string Txid { get; set; } = "";
    public string PixCopiaCola { get; set; } = "";
    public string QrCodeBase64 { get; set; } = "";
    public DateTime ExpiraEm { get; set; }
    public decimal Valor { get; set; }
}

public class DashboardFinanceiroApi
{
    public decimal TotalAVencer30dPagar { get; set; }
    public decimal TotalAVencer30dReceber { get; set; }
    public decimal TotalVencidoPagar { get; set; }
    public decimal TotalVencidoReceber { get; set; }
    public decimal TotalPagoMes { get; set; }
    public decimal TotalRecebidoMes { get; set; }
    public int QtdContasPagarAbertas { get; set; }
    public int QtdContasReceberAbertas { get; set; }
    public int QtdParcelasVencidasHoje { get; set; }
}
