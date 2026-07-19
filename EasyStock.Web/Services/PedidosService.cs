using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>Onda P2 — UI Web do módulo Pedido (encomenda).</summary>
public class PedidosService(ApiClient api, SessionService session) : TenantServiceBase(session)
{



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
        DateTime? desde = null, DateTime? ate = null, string? search = null, string? sort = null)
    {
        // issue 958: pedia 500, mas ListarPedidosUseCase clampa em 200 (Math.Clamp) — o
        // excedente era truncado silenciosamente, sem erro nem sinal pro cockpit/KDS.
        // Alinhado ao cap real ate a listagem virar paginacao server-side de verdade.
        var qs = $"pedidos?empresaId={GetEmpresaId()}&page=1&pageSize=200";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        if (clienteId.HasValue && clienteId.Value != Guid.Empty) qs += $"&clienteId={clienteId}";
        if (desde.HasValue) qs += $"&desde={Uri.EscapeDataString(desde.Value.ToString("o"))}";
        if (ate.HasValue)   qs += $"&ate={Uri.EscapeDataString(ate.Value.ToString("o"))}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrEmpty(sort)) qs += $"&sort={Uri.EscapeDataString(sort)}";
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

    /// <summary>Venda balcao atomica: passa lojaId da sessao (necessario pra saida de estoque).</summary>
    public Task<ApiResult<BalcaoResultApi>> FinalizarBalcaoAsync(EasyStock.Web.Controllers.CriarBalcaoWebRequest req)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<BalcaoResultApi>());
        Guid? lojaId = Guid.TryParse(Session.GetLojaId(), out var l) && l != Guid.Empty ? l : (Guid?)null;
        return api.PostAsync<BalcaoResultApi>("pedidos/balcao", new
        {
            empresaId,
            lojaId,
            clienteId = req.ClienteId,
            novoClienteNome = req.NovoClienteNome,
            novoClienteApt = req.NovoClienteApt,
            novoClienteTelefone = req.NovoClienteTelefone,
            clienteNomeAdHoc = req.NomeAdHoc,
            itens = req.Itens,
            pagou = req.Pagou,
            formaPagamento = req.FormaPagamento,
            observacoes = req.Observacoes,
            origem = "balcao"
        });
    }

    public Task<ApiResult<Pedido>> AlterarAgendamentoAsync(string id, DateTime? agendadoParaEm)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        if (!Guid.TryParse(id, out var pedidoId)) return Task.FromResult(IdErr<Pedido>());
        return api.PatchAsync<Pedido>($"pedidos/{id}/agendamento", new
        {
            empresaId, pedidoId, agendadoParaEm, origem = "web"
        });
    }

    public Task<ApiResult<Pedido>> AtualizarStatusAsync(string id, string status)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        if (!Guid.TryParse(id, out var pedidoId)) return Task.FromResult(IdErr<Pedido>());
        return api.PatchAsync<Pedido>($"pedidos/{id}/status", new
        {
            id = pedidoId, empresaId, status, origem = "web"
        });
    }

    public Task<ApiResult<Pedido>> CancelarAsync(string id, string? motivo)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        if (!Guid.TryParse(id, out var pedidoId)) return Task.FromResult(IdErr<Pedido>());
        return api.PostAsync<Pedido>($"pedidos/{id}/cancelar", new
        {
            id = pedidoId, empresaId, motivo, origem = "web"
        });
    }

    public Task<ApiResult<Pedido>> AdicionarItemAsync(string id,
        string nome, decimal quantidade, decimal precoUnitario,
        Guid? produtoId, string? emoji, string? unidade, string? observacao)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        if (!Guid.TryParse(id, out var pedidoId)) return Task.FromResult(IdErr<Pedido>());
        return api.PostAsync<Pedido>($"pedidos/{id}/itens", new
        {
            empresaId, pedidoId,
            nome, quantidade, precoUnitario, produtoId, emoji, unidade, observacao,
            origem = "web"
        });
    }

    public Task<ApiResult<bool>> RemoverItemAsync(string id, string itemId) =>
        api.DeleteAsync($"pedidos/{id}/itens/{itemId}?empresaId={GetEmpresaId()}");

    // issue 962: permitirExcedente=true SOMENTE quando o caller ja mostrou o aviso de
    // gorjeta/arredondamento (Detail.cshtml). O cockpit chama com o default false --
    // mantem o bloqueio duro la, onde o valor costuma vir pre-preenchido de uma listagem
    // que pode estar desatualizada e o risco de fat-finger no balcao e maior.
    public Task<ApiResult<Pedido>> RegistrarPagamentoAsync(string id,
        string metodo, decimal valor, string? referencia, string? observacao,
        bool permitirExcedente = false)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Pedido>());
        if (!Guid.TryParse(id, out var pedidoId)) return Task.FromResult(IdErr<Pedido>());
        return api.PostAsync<Pedido>($"pedidos/{id}/pagamentos", new
        {
            empresaId, pedidoId,
            metodo, valor, referencia, observacao, origem = "web", permitirExcedente
        });
    }

    public Task<ApiResult<bool>> RemoverPagamentoAsync(string id, string pagamentoId) =>
        api.DeleteAsync($"pedidos/{id}/pagamentos/{pagamentoId}?empresaId={GetEmpresaId()}");

    // ── Aprovação storefront (#862) ───────────────────────────────────
    // Reusa os endpoints prontos da API (AprovacaoPedidoController): EmpresaId/UsuarioId
    // saem do token (currentUser), não do body — por isso só validamos a sessão aqui.

    public Task<ApiResult<AprovacaoResult>> AprovarAsync(string id, string? observacoes = null)
    {
        if (GetEmpresaId() == Guid.Empty) return Task.FromResult(EmpresaErr<AprovacaoResult>());
        if (!Guid.TryParse(id, out _)) return Task.FromResult(IdErr<AprovacaoResult>());
        return api.PostAsync<AprovacaoResult>($"storefront/pedidos/{id}/aprovar", new { observacoes });
    }

    public Task<ApiResult<AprovacaoResult>> RecusarAsync(string id, string motivo, string? mensagemCliente = null)
    {
        if (GetEmpresaId() == Guid.Empty) return Task.FromResult(EmpresaErr<AprovacaoResult>());
        if (!Guid.TryParse(id, out _)) return Task.FromResult(IdErr<AprovacaoResult>());
        return api.PostAsync<AprovacaoResult>($"storefront/pedidos/{id}/recusar", new { motivo, mensagemCliente });
    }

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
