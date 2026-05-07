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
}
