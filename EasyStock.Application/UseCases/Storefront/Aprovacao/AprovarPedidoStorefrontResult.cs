namespace EasyStock.Application.UseCases.Storefront.Aprovacao;

/// <summary>Resultado da aprovação (DTO de retorno do endpoint POST /aprovar).</summary>
/// <param name="PedidoId">PK do pedido aprovado.</param>
/// <param name="Status">Sempre <c>"aprovado_baba"</c> no caminho feliz.</param>
/// <param name="AprovadoEm">Timestamp UTC da transição.</param>
/// <param name="AprovadoPor">Nome amigável do operador (fallback: UsuarioId).</param>
/// <param name="NotificacaoCliente">Sinaliza enfileiramento do evento WhatsApp.</param>
public sealed record AprovarPedidoStorefrontResult(
    Guid PedidoId,
    string Status,
    DateTime AprovadoEm,
    string AprovadoPor,
    NotificacaoCliente NotificacaoCliente);

/// <summary>Subobjeto comum a aprovar/recusar — sinaliza Outbox enfileirado.</summary>
public sealed record NotificacaoCliente(bool Enfileirada, string Evento);
