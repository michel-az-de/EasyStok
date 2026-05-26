using EasyStock.Domain.Events;

namespace EasyStock.Application.Events.Storefront;

/// <summary>
/// Enfileirado no Outbox quando a Babá aprova um pedido Storefront (TASK-EZ-APROVAR-001).
/// Handler chamará WhatsApp Cloud API com template <c>pedido_aprovado</c>
/// (template precisa ser aprovado pela Meta — bloqueado por TASK-HUM-001).
///
/// <para>
/// Payload mantém apenas IDs + nome/telefone snapshot pra evitar lookup no handler
/// (Outbox dispatcher é desacoplado da request).
/// </para>
/// </summary>
public sealed record NotificarClientePedidoAprovadoEvent(
    Guid PedidoId,
    Guid EmpresaId,
    Guid? ClienteId,
    string? ClienteNome,
    string? ClienteTelefone,
    DateTime AprovadoEm)
    : DomainEvent(Guid.NewGuid(), DateTime.UtcNow);
