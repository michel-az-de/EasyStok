using EasyStock.Domain.Sales;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Ordenação "urgência" da lista de Pedidos — usada pelo cockpit operacional (issue #591).
///
/// <para>
/// Invariante crítico (TIER1-3 da revisão): pedidos ABERTOS vêm sempre antes dos
/// TERMINAIS (entregue/cancelado). Como a listagem é paginada com um cap (pageSize),
/// isso garante que o cap descarte histórico terminal — <b>nunca</b> um pedido ativo
/// (ex.: um "aguardando" de dias atrás não é empurrado pra fora por terminais recentes).
/// </para>
///
/// <para>
/// Refino de exibição por cima, dentro de cada grupo: (2) atrasados (agendado vencido)
/// primeiro; (3) agendados antes de sem-agenda, ordenados pelo horário ascendente;
/// (4) desempate por mais recentes. As constantes de status são <c>const string</c>
/// (<see cref="StatusPedidoMapper"/>) → a expressão é traduzível pelo Npgsql e também
/// roda em LINQ-to-objects (testável sem banco).
/// </para>
/// </summary>
public static class PedidoOrdering
{
    public static IOrderedQueryable<Pedido> PorUrgencia(IQueryable<Pedido> query, DateTime agoraUtc) =>
        query
            // 1) abertos (0) antes de terminais (1) — garante que o cap não derrube ativo.
            .OrderBy(p => p.Status == StatusPedidoMapper.Entregue
                       || p.Status == StatusPedidoMapper.Cancelado ? 1 : 0)
            // 2) atrasados (agendado vencido) primeiro.
            .ThenBy(p => p.AgendadoParaEm != null && p.AgendadoParaEm < agoraUtc ? 0 : 1)
            // 3) agendados antes de sem-agenda; agendados por horário ascendente.
            .ThenBy(p => p.AgendadoParaEm == null)
            .ThenBy(p => p.AgendadoParaEm)
            // 4) desempate: mais recentes primeiro.
            .ThenByDescending(p => p.CriadoEm);
}
