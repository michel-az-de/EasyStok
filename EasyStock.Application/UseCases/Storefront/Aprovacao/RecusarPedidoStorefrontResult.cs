namespace EasyStock.Application.UseCases.Storefront.Aprovacao;

/// <summary>Resultado da recusa (DTO de retorno do endpoint POST /recusar).</summary>
public sealed record RecusarPedidoStorefrontResult(
    Guid PedidoId,
    string Status,
    DateTime RecusadoEm,
    string RecusadoPor,
    string Motivo,
    bool VagaLiberada,
    RefundEnfileirado Refund,
    NotificacaoCliente NotificacaoCliente);

/// <summary>Sinaliza enfileiramento do evento de refund automático no Outbox.</summary>
public sealed record RefundEnfileirado(bool Enfileirado, string Evento);
