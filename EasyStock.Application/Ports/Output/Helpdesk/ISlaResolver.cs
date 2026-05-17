using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Helpdesk;

public sealed record SlaResolvido(
    int MinutosResposta,
    int MinutosResolucao,
    DateTime PrazoResposta,
    DateTime PrazoResolucao);

/// <summary>
/// Resolve a configuracao de SLA aplicavel a uma empresa+prioridade. Hierarquia:
/// (1) Override por empresa, (2) por plano da assinatura ativa, (3) default global,
/// (4) fallback hardcoded. Usado tanto pelo fluxo cliente (UseCase) quanto pelo
/// fluxo admin (Service).
/// </summary>
public interface ISlaResolver
{
    Task<SlaResolvido> ResolverAsync(
        Guid empresaId,
        TicketPrioridade prioridade,
        DateTime? referencia = null,
        CancellationToken ct = default);
}
