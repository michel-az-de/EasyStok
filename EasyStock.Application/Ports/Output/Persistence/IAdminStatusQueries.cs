namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read model do painel de status Admin SaaS. Concentra o health-check do banco
/// + métricas agregadas cross-tenant (erros, usuários ativos, uso de IA, tickets)
/// que antes viviam direto no AdminStatusController (F7).
/// </summary>
public interface IAdminStatusQueries
{
    Task<AdminStatusData> GetStatusAsync(DateTime nowUtc, CancellationToken ct = default);
}

public sealed record AdminStatusData(
    string DbStatus,
    long DbLatencyMs,
    int Erros24h,
    int Erros1h,
    int UsuariosAtivos24h,
    int IaGeracoesMes,
    int TicketsAbertos,
    IReadOnlyList<AdminStatusErroRecente> ErrosRecentes);

public sealed record AdminStatusErroRecente(
    string Acao,
    string? Detalhes,
    DateTime DataHora);
