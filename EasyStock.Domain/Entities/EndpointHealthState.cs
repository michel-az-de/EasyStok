using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities;

/// <summary>
/// Estado de saude de cada endpoint critico monitorado pelo
/// EndpointHealthMonitorService (Worker). Persiste contadores e timestamps
/// pra (1) sobreviver a restart do worker e (2) dar idempotencia ao alerta
/// automatico (so abre 1 ticket por endpoint a cada 24h).
///
/// Nao tem EmpresaId — e saude da plataforma, nao de tenants. Ticket gerado
/// sai pra empresa interna (Ci:OwnerEmpresaId), igual ao /api/ci/tickets.
/// </summary>
[Table("endpoint_health_state")]
public class EndpointHealthState
{
    [Key]
    [MaxLength(128)]
    [Column("endpoint_name")]
    public string EndpointName { get; set; } = default!;

    [Column("consecutive_failures")]
    public int ConsecutiveFailures { get; set; }

    [Column("last_check_at")]
    public DateTime? LastCheckAt { get; set; }

    [Column("last_failure_at")]
    public DateTime? LastFailureAt { get; set; }

    [Column("last_failure_message")]
    [MaxLength(512)]
    public string? LastFailureMessage { get; set; }

    [Column("last_alerted_at")]
    public DateTime? LastAlertedAt { get; set; }

    [Column("last_alerted_ticket_id")]
    public Guid? LastAlertedTicketId { get; set; }

    [Column("criado_em")]
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    [Column("atualizado_em")]
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
