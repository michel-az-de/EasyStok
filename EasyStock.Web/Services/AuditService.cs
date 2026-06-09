using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>
/// Proxy BFF para a trilha de auditoria (entity_alteracoes) exposta pela Api em
/// <c>/api/audit/*</c>. O Web reexpoe os mesmos paths para o browser porque o app host
/// nao roteia <c>/api/*</c> direto pra Api e o Web ja tem rotas proprias sob <c>/api/</c>
/// (ex.: <c>/api/assinatura</c>). Espelha <see cref="AssinaturaService"/>; auth via sessao
/// server-side (Bearer injetado no <see cref="ApiClient"/>).
/// </summary>
public class AuditService(ApiClient api)
{
    public Task<ApiResult<object>> PorEntidadeAsync(string tipoEntidade, Guid entidadeId, int page, int pageSize) =>
        api.GetAsync<object>($"audit/entity/{Uri.EscapeDataString(tipoEntidade)}/{entidadeId}?page={page}&pageSize={pageSize}");

    public Task<ApiResult<object>> TimelineClienteAsync(Guid clienteId, int page, int pageSize) =>
        api.GetAsync<object>($"audit/client-timeline/{clienteId}?page={page}&pageSize={pageSize}");
}
