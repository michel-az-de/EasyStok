using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Dispositivo móvel pareado a uma empresa/loja do EasyStock.
///
/// Fluxo de pareamento:
///   1. Operador no painel web gera código curto (<see cref="PairingCode"/>),
///      válido por <see cref="PairingExpiresAt"/>.
///   2. App lê o código (digita ou QR), chama <c>POST /api/mobile/devices/pair</c>
///      enviando <c>{ pairingCode, deviceId }</c>.
///   3. Servidor valida, persiste o vínculo, gera uma <see cref="ApiKey"/>
///      única (UUID) e devolve pro app, junto com EmpresaId/LojaId/OperatorName.
///   4. App passa a enviar <c>X-Mobile-Api-Key: {apiKey}</c> em todo request.
///
/// Revogação:
///   - Manual via /dispositivos (operador clica "Revogar")
///   - Marca <see cref="Revoked"/>=true; rotação preserva auditoria histórica.
///
/// Multi-tenancy:
///   Todas as <c>mobile_*</c> entities passam a ter EmpresaId+LojaId; o sync
///   resolve esses campos a partir do device autenticado e injeta nas mutations.
/// </summary>
[Table("mobile_devices")]
public class MobileDevice
{
    /// <summary>
    /// Id estável vindo do app (UUID gerado no primeiro boot, persiste em
    /// <c>cdb-device-id</c> no localStorage do PWA). Usado como chave para
    /// identificar o device entre re-pareamentos.
    /// </summary>
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>
    /// SHA-256 da chave secreta enviada no header X-Mobile-Api-Key. O plaintext
    /// é gerado no pareamento (UUID), devolvido uma única vez ao app, e nunca
    /// persistido — em breach o atacante não consegue se passar pelo device.
    /// Lookup do middleware: hash do header e busca por <c>api_key_hash</c>.
    /// </summary>
    [Required, MaxLength(128)]
    [Column("api_key_hash")]
    public string ApiKeyHash { get; set; } = default!;

    /// <summary>Empresa proprietária do device. Resolve multi-tenant.</summary>
    [Column("empresa_id")]
    public Guid EmpresaId { get; set; }

    /// <summary>Loja específica dentro da empresa. Pedidos/lotes do device caem nessa loja.</summary>
    [Column("loja_id")]
    public Guid LojaId { get; set; }

    /// <summary>
    /// Usuário do EasyStock que pareou (gerou o código). Audit trail de quem autorizou.
    /// Não usado pra autenticar mutations — o "operador" do app é informado em runtime.
    /// </summary>
    [Column("paired_by_user_id")]
    public Guid? PairedByUserId { get; set; }

    /// <summary>Nome amigável (ex: "iPhone 13 Cozinha", "Tablet caixa"). Operador define no pareamento.</summary>
    [MaxLength(120)]
    [Column("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Nome do operador padrão pareado neste device. App pode trocar em runtime;
    /// este campo é só sugestão inicial.
    /// </summary>
    [MaxLength(64)]
    [Column("default_operator_name")]
    public string? DefaultOperatorName { get; set; }

    /// <summary>Código curto (6 dígitos) usado uma única vez no pareamento. NULL após uso.</summary>
    [MaxLength(16)]
    [Column("pairing_code")]
    public string? PairingCode { get; set; }

    /// <summary>Expiração do código de pareamento. Após isso o código vira inválido.</summary>
    [Column("pairing_expires_at")]
    public DateTime? PairingExpiresAt { get; set; }

    /// <summary>Timestamp do pareamento efetivo (quando app trocou código por apiKey).</summary>
    [Column("paired_at")]
    public DateTime? PairedAt { get; set; }

    /// <summary>Última vez que o device fez request com api key válida. Atualizado por <see cref="MobileApiKeyAttribute"/>.</summary>
    [Column("last_seen_at")]
    public DateTime? LastSeenAt { get; set; }

    /// <summary>IP do último request — pra detectar mudanças de rede / fraude.</summary>
    [MaxLength(64)]
    [Column("last_seen_ip")]
    public string? LastSeenIp { get; set; }

    /// <summary>True quando admin revogou o pareamento. Requests passam a falhar com 401.</summary>
    [Column("revoked")]
    public bool Revoked { get; set; }

    [Column("revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [Column("revoked_by_user_id")]
    public Guid? RevokedByUserId { get; set; }

    [MaxLength(512)]
    [Column("push_token")]
    public string? PushToken { get; set; }

    /// <summary>
    /// Canary device — recebe versao nova do PWA/APK antes da frota geral.
    /// Casa da Baba inteira fica em stable por default; admin marca 1-2 devices
    /// internos como canary pra validar update sem afetar producao.
    /// </summary>
    [Column("is_canary")]
    public bool IsCanary { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
