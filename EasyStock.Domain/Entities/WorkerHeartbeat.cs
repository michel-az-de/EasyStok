namespace EasyStock.Domain.Entities;

/// <summary>
/// Estado atual de execução de cada hosted service do Worker. Tabela com 1 linha por
/// <see cref="Servico"/> (UPSERT a cada tick) — guarda o último tick visto, status,
/// itens processados e duração. NÃO há histórico (versão 1); pra séries temporais
/// usar OpenTelemetry/Grafana.
/// <para>
/// Read-path: Admin/Operacao/Worker via <c>GET /api/admin/worker-status</c>.
/// Write-path: <c>IHeartbeatRecorder</c> chamado no fim de cada loop dos 7 hosted
/// services (SlaMonitor, IntegrationOutbox, Dispatcher x4, Avaliador, Coletor,
/// PostgresOutboxSignaler, AnonimizarLogs).
/// </para>
/// </summary>
public sealed class WorkerHeartbeat
{
    public Guid Id { get; set; }

    /// <summary>Identificador estável do job. Ex: "SlaMonitor", "Dispatcher-shard-0",
    /// "Avaliador", "Coletor", "IntegrationOutbox", "OutboxSignaler", "AnonimizarLogs".</summary>
    public string Servico { get; set; } = "";

    /// <summary>Última vez que o job completou um tick (UTC).</summary>
    public DateTime UltimoTickEm { get; set; }

    /// <summary>"OK" | "Erro" | "Skip" (lock detido por outra réplica).</summary>
    public string Status { get; set; } = "OK";

    /// <summary>Mensagem opcional (curta). Em caso de erro: tipo + 1ª linha do erro.</summary>
    public string? Detalhe { get; set; }

    /// <summary>Contador opcional (mensagens despachadas, tickets avaliados etc).</summary>
    public int? ItensProcessados { get; set; }

    /// <summary>Duração do tick em ms (opcional).</summary>
    public int? DuracaoMs { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }
}
