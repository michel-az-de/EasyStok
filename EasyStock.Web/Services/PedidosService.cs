using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>Onda P2 — UI Web do módulo Pedido (encomenda).</summary>
public class PedidosService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private static ApiResult<T> EmpresaErr<T>() =>
        ApiResult<T>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");

    public Task<ApiResult<PagedResult<Pedido>>> ListarPaginadoAsync(
        int page = 1, int pageSize = 30,
        string? status = null, Guid? clienteId = null,
        DateTime? desde = null, DateTime? ate = null, string? search = null)
    {
        var qs = $"pedidos?empresaId={GetEmpresaId()}&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        if (clienteId.HasValue && clienteId.Value != Guid.Empty) qs += $"&clienteId={clienteId}";
        if (desde.HasValue) qs += $"&desde={Uri.EscapeDataString(desde.Value.ToString("o"))}";
        if (ate.HasValue)   qs += $"&ate={Uri.EscapeDataString(ate.Value.ToString("o"))}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync<PagedResult<Pedido>>(qs);
    }

    public Task<ApiResult<List<Pedido>>> ListarAsync(
        string? status = null, Guid? clienteId = null,
        DateTime? desde = null, DateTime? ate = null, string? search = null)
    {
        var qs = $"pedidos?empresaId={GetEmpresaId()}&page=1&pageSize=500";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        if (clienteId.HasValue && clienteId.Value != Guid.Empty) qs += $"&clienteId={clienteId}";
        if (desde.HasValue) qs += $"&desde={Uri.EscapeDataString(desde.Value.ToString("o"))}";
        if (ate.HasValue)   qs += $"&ate={Uri.EscapeDataString(ate.Value.ToString("o"))}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync<List<Pedido>>(qs);
    }

    public Task<ApiResult<PedidoDetalhe>> ObterAsync(string id) =>
        api.GetAsync<PedidoDetalhe>($"pedidos/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<Pedido>> CriarAsync(
        Guid? clienteId, string? nomeAdHoc, string? aptAdHoc, string? telefoneAdHoc,
        string? observacoes, List<CriarItemInput>? itens, DateTime? agendadoParaEm = null)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        return api.PostAsync<Pedido>("pedidos", new
        {
            empresaId,
            clienteId,
            clienteNomeAdHoc = nomeAdHoc,
            clienteAptAdHoc = aptAdHoc,
            clienteTelefoneAdHoc = telefoneAdHoc,
            observacoes,
            origem = "web",
            itens,
            agendadoParaEm
        });
    }

    public Task<ApiResult<Pedido>> AlterarAgendamentoAsync(string id, DateTime? agendadoParaEm)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        return api.PatchAsync<Pedido>($"pedidos/{id}/agendamento", new
        {
            empresaId, pedidoId = Guid.Parse(id), agendadoParaEm, origem = "web"
        });
    }

    public Task<ApiResult<Pedido>> AtualizarStatusAsync(string id, string status)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        return api.PatchAsync<Pedido>($"pedidos/{id}/status", new
        {
            id = Guid.Parse(id), empresaId, status, origem = "web"
        });
    }

    public Task<ApiResult<Pedido>> CancelarAsync(string id, string? motivo)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        return api.PostAsync<Pedido>($"pedidos/{id}/cancelar", new
        {
            id = Guid.Parse(id), empresaId, motivo, origem = "web"
        });
    }

    public Task<ApiResult<Pedido>> AdicionarItemAsync(string id,
        string nome, decimal quantidade, decimal precoUnitario,
        Guid? produtoId, string? emoji, string? unidade, string? observacao)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        return api.PostAsync<Pedido>($"pedidos/{id}/itens", new
        {
            empresaId, pedidoId = Guid.Parse(id),
            nome, quantidade, precoUnitario, produtoId, emoji, unidade, observacao,
            origem = "web"
        });
    }

    public Task<ApiResult<bool>> RemoverItemAsync(string id, string itemId) =>
        api.DeleteAsync($"pedidos/{id}/itens/{itemId}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<Pedido>> RegistrarPagamentoAsync(string id,
        string metodo, decimal valor, string? referencia, string? observacao)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        return api.PostAsync<Pedido>($"pedidos/{id}/pagamentos", new
        {
            empresaId, pedidoId = Guid.Parse(id),
            metodo, valor, referencia, observacao, origem = "web"
        });
    }

    public Task<ApiResult<bool>> RemoverPagamentoAsync(string id, string pagamentoId) =>
        api.DeleteAsync($"pedidos/{id}/pagamentos/{pagamentoId}?empresaId={GetEmpresaId()}");

    // ── Mobile ────────────────────────────────────────────────────────

    public Task<ApiResult<List<MobilePedidoSummary>>> ListarMobileAsync(bool pendingOnly = false, string? status = null)
    {
        var qs = $"mobile/orders?empresaId={GetEmpresaId()}";
        if (pendingOnly) qs += "&pendingOnly=true";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        return api.GetAsync<List<MobilePedidoSummary>>(qs);
    }

    public Task<ApiResult<object>> LinkMobileAsync(string mobileOrderId, Guid? erpPedidoId) =>
        api.PostAsync<object>($"mobile/orders/{mobileOrderId}/link", new { erpPedidoId });

    public Task<ApiResult<object>> UnlinkMobileAsync(string mobileOrderId) =>
        api.PostAsync<object>($"mobile/orders/{mobileOrderId}/unlink", new { });
}

public record CriarItemInput(string Nome, decimal Quantidade, decimal PrecoUnitario,
    Guid? ProdutoId = null, string? Emoji = null, string? Unidade = null, string? Observacao = null);
