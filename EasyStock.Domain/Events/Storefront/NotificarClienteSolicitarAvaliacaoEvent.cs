namespace EasyStock.Domain.Events.Storefront;

/// <summary>
/// Emitido pelo background service +24h após entrega para solicitar avaliação ao cliente.
/// Handler <c>EnviarLinkAvaliacaoWhatsAppHandler</c> envia o link via WhatsApp.
/// </summary>
public sealed record NotificarClienteSolicitarAvaliacaoEvent(
    Guid PedidoId,
    Guid ClienteId,
    Guid EmpresaId,
    string TelefoneCliente,
    string NomeCliente,
    string Slug)
    : DomainEvent(Guid.NewGuid(), DateTime.UtcNow);
