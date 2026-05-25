using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class NotasFiscaisService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<PagedResult<NfeListItem>>> ListarAsync(
        int page = 1, string? status = null, string? desde = null, string? ate = null, string? search = null)
    {
        var qs = $"notas-fiscais?empresaId={GetEmpresaId()}&page={page}&pageSize=20";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(desde)) qs += $"&desde={Uri.EscapeDataString(desde)}";
        if (!string.IsNullOrEmpty(ate)) qs += $"&ate={Uri.EscapeDataString(ate)}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync<PagedResult<NfeListItem>>(qs);
    }

    public Task<ApiResult<NfeDetalhe>> ObterAsync(Guid id)
    {
        return api.GetAsync<NfeDetalhe>($"notas-fiscais/{id}?empresaId={GetEmpresaId()}");
    }

    public Task<ApiResult<object>> CancelarAsync(Guid id, string motivo)
    {
        return api.PostAsync<object>($"notas-fiscais/{id}/cancelar?empresaId={GetEmpresaId()}", new { motivo });
    }

    public Task<ApiResult<List<PedidoElegivelItem>>> ListarPedidosElegiveisAsync(int limit = 50)
    {
        return api.GetAsync<List<PedidoElegivelItem>>(
            $"notas-fiscais/pedidos-elegiveis?empresaId={GetEmpresaId()}&limit={limit}");
    }

    public Task<ApiResult<NfeListItem>> EmitirDePedidoAsync(
        Guid pedidoId, string? destinatarioCpf, string? destinatarioNome, string? destinatarioEmail)
    {
        var body = new
        {
            pedidoId,
            destinatarioCpf = string.IsNullOrWhiteSpace(destinatarioCpf) ? null : destinatarioCpf,
            destinatarioNome = string.IsNullOrWhiteSpace(destinatarioNome) ? null : destinatarioNome,
            destinatarioEmail = string.IsNullOrWhiteSpace(destinatarioEmail) ? null : destinatarioEmail,
        };
        return api.PostAsync<NfeListItem>(
            $"notas-fiscais/emitir-de-pedido?empresaId={GetEmpresaId()}", body);
    }
}

public class PedidoElegivelItem
{
    public Guid Id { get; set; }
    public DateTime CriadoEm { get; set; }
    public string ClienteNome { get; set; } = "Consumidor";
    public decimal Total { get; set; }
    public int QtdItens { get; set; }
    public string? Status { get; set; }
}
