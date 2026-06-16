using System.Linq.Expressions;
using EasyStock.Domain.Entities.Mobile;

namespace EasyStock.Application.Common;

/// <summary>
/// Criterios operacionais do dia compartilhados entre o cockpit por loja
/// (OperationController.GetDashboard) e o Centro de Comando da Frota
/// (FleetOperationQueries). Fonte unica dos predicados/limiares para garantir
/// PARIDADE: o que conta como "entregue hoje", "aberto", "travado" etc. e definido
/// aqui uma vez e usado nos dois lados (issue 623).
///
/// Os predicados sao Expression para traduzir no SQL (frota, GROUP BY) e Compile()
/// no cockpit (contagem em memoria) a partir da MESMA logica. Sem relogio ambiente:
/// o instante 'now'/'inicioDiaUtc' entra por parametro (AmbientClockBan / ADR-0032).
/// </summary>
public static class OperacaoCriterios
{
    // Status do mobile Order (mobile_orders) — strings legadas.
    public const string StatusEntregue = "entregue";
    public const string StatusCancelado = "cancelado";
    public const string StatusPreparando = "preparando";
    public const string StatusPronto = "pronto";

    /// <summary>Minutos sem progresso ate um pedido "preparando" contar como travado.</summary>
    public const int MinutosTravado = 30;

    /// <summary>Minutos desde o ultimo contato ate um device contar como inativo.</summary>
    public const int MinutosDeviceAtivo = 30;

    /// <summary>Pedido entregue dentro do dia operacional (coluna de instante UpdatedAt).</summary>
    public static Expression<Func<Order, bool>> EntregueHoje(DateTime inicioDiaUtc)
        => o => o.Status == StatusEntregue && o.UpdatedAt >= inicioDiaUtc;

    /// <summary>Pedido em aberto (nem entregue nem cancelado).</summary>
    public static Expression<Func<Order, bool>> Aberto()
        => o => o.Status != StatusEntregue && o.Status != StatusCancelado;

    /// <summary>Pedido "preparando" parado ha mais de <see cref="MinutosTravado"/> min.</summary>
    public static Expression<Func<Order, bool>> Travado(DateTime nowUtc)
    {
        var limite = nowUtc.AddMinutes(-MinutosTravado);
        return o => o.Status == StatusPreparando && (o.UpdatedAt < limite || o.CreatedAt < limite);
    }

    /// <summary>Pedido "pronto" sem confirmacao de conferencia.</summary>
    public static Expression<Func<Order, bool>> ConferenciaPendente()
        => o => o.Status == StatusPronto && o.ConfirmedAt == null;

    /// <summary>Device contavel para a frota (nao revogado).</summary>
    public static Expression<Func<MobileDevice, bool>> DeviceContavel()
        => d => !d.Revoked;

    /// <summary>Device ativo (nao revogado e visto nos ultimos <see cref="MinutosDeviceAtivo"/> min).</summary>
    public static Expression<Func<MobileDevice, bool>> DeviceAtivo(DateTime nowUtc)
    {
        var limite = nowUtc.AddMinutes(-MinutosDeviceAtivo);
        return d => !d.Revoked && d.LastSeenAt != null && d.LastSeenAt >= limite;
    }
}
