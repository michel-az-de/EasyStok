using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>
/// Onda 1.1 — wrapper sobre <see cref="ApiClient"/> para abertura de tickets
/// pelo Web (operador-cliente reportando problema sobre pedido especifico).
/// Endpoint: POST /api/tickets (autenticado, multi-tenant).
/// </summary>
public sealed class TicketsApiService(ApiClient api)
{
    public Task<ApiResult<AbrirTicketResultDto>> AbrirAsync(string titulo, string descricao, string categoria, Guid? pedidoId = null, Guid? faturaId = null) =>
        api.PostAsync<AbrirTicketResultDto>("tickets", new
        {
            titulo,
            descricao,
            categoria,
            pedidoId,
            faturaId,
            canalOrigem = "Web"
        });
}

public sealed record AbrirTicketResultDto(Guid TicketId, string Status, DateTime CriadoEm);
