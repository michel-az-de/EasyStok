using EasyStock.Domain.Events;

namespace EasyStock.Application.Events.Storefront;

/// <summary>
/// Enfileirado no Outbox quando a Babá recusa um pedido Storefront (TASK-EZ-APROVAR-001).
/// Handler enviará WhatsApp ao cliente com a <c>MensagemCliente</c> + indicação
/// de que o estorno está sendo processado.
/// </summary>
public sealed record NotificarClientePagamentoRecusadoEvent(
    Guid PedidoId,
    Guid EmpresaId,
    Guid? ClienteId,
    string? ClienteNome,
    string? ClienteTelefone,
    string Motivo,
    string? MensagemCliente,
    DateTime RecusadoEm)
    : DomainEvent(Guid.NewGuid(), DateTime.UtcNow);
