using EasyStock.Domain.Events;

namespace EasyStock.Application.Events.Pedidos;

/// <summary>
/// Enfileirado no Outbox a cada transição de status do pedido no fluxo operacional
/// (ADR-0042, Onda 4 / issue 866). É o <strong>ponto de integração único</strong> da esteira:
/// Hiram (notificações) e marketplaces consomem ESTE evento em vez de observar a coluna
/// <c>Status</c> do pedido.
///
/// <para>
/// <c>StatusAntigo</c>/<c>StatusNovo</c> são as strings canônicas do
/// <see cref="Domain.Sales.StatusPedidoMapper"/> (ex.: <c>"aguardando"</c>, <c>"preparando"</c>) —
/// contrato de fio, NÃO rótulo de UI. Publicado dentro da mesma transação do commit de status
/// (atômico): ou a mudança de status e o evento persistem juntos, ou nenhum.
/// </para>
///
/// <para>
/// TipoEvento no outbox: <c>"pedido.mudou_status"</c>. Consumidores atuais: o handler de
/// observabilidade (<see cref="Handlers.PedidoMudouStatusLogHandler"/>). A Onda 5 (#867)
/// adiciona os handlers de Hiram/marketplace ao lado (o dispatcher roda todos em sequência).
/// </para>
/// </summary>
public sealed record PedidoMudouStatusEvent(
    Guid PedidoId,
    Guid EmpresaId,
    Guid? LojaId,
    string StatusAntigo,
    string StatusNovo,
    string? Origem,
    Guid? UsuarioId,
    string? UsuarioNome,
    DateTime OcorridoEm)
    : DomainEvent(Guid.NewGuid(), OcorridoEm);
