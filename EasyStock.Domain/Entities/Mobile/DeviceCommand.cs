using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Comando remoto enfileirado pra um dispositivo (Onda 4).
///
/// Fluxo:
///   1. Gestor no painel /operacao dispara comando (POST /devices/{id}/commands).
///   2. Servidor persiste aqui.
///   3. Device, na próxima chamada de /sync ou /sync/pull, recebe lista de
///      comandos pendentes (delivered_at = null).
///   4. App executa, marca como entregue (PATCH ou via header de ack).
///
/// Sem WebSocket nesta onda — entrega é "best-effort no próximo polling".
/// Onda 5 evolui pra SignalR + push notifications.
/// </summary>
[Table("mobile_device_commands")]
public class DeviceCommand
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(64)]
    [Column("device_id")]
    public string DeviceId { get; set; } = default!;

    [Column("empresa_id")]
    public Guid EmpresaId { get; set; }

    /// <summary>
    /// Tipo do comando. Valores conhecidos:
    /// <c>flush_now</c>, <c>pull_now</c>, <c>reload</c>, <c>message</c>.
    /// </summary>
    [Required, MaxLength(32)]
    [Column("command_type")]
    public string CommandType { get; set; } = default!;

    /// <summary>JSON livre — ex: {"text":"oi felipe"} pra command_type=message.</summary>
    [Column("payload_json")]
    public string? PayloadJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("created_by_user_id")]
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Quando o device pediu o comando via /sync. NULL = ainda na fila.</summary>
    [Column("delivered_at")]
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Quando o device confirmou execução. Opcional — nem todo comando ack.</summary>
    [Column("executed_at")]
    public DateTime? ExecutedAt { get; set; }

    /// <summary>Auto-expira pra não ficar comando ghost na fila. Default 24h.</summary>
    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }
}
