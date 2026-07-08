namespace EasyStock.Web.Models.Api;

/// <summary>
/// Retorno enxuto dos endpoints de aprovação/recusa storefront da API
/// (<c>POST api/storefront/pedidos/{id}/aprovar|recusar</c>). O cockpit (#862) só
/// precisa do <see cref="Status"/> novo pra re-renderizar a linha — aprovar/recusar
/// não mexem em pagamento nem itens. Campos extras da API (timestamps, refund,
/// notificação) são ignorados na desserialização.
/// </summary>
public sealed record AprovacaoResult(string PedidoId, string Status);
