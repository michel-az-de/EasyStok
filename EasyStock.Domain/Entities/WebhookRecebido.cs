namespace EasyStock.Domain.Entities;

/// <summary>
/// Registro de webhook recebido de gateway de pagamento — usado para
/// idempotencia e auditoria.
///
/// <para>
/// Chave UNIQUE em <c>(Provedor, EventIdExterno)</c>: se o mesmo evento chega
/// duas vezes (Efi reenvia ate 5x em 24h se nao receber 2xx rapido), o segundo
/// INSERT falha e o controller retorna 200 idempotente sem reprocessar.
/// </para>
///
/// <para>
/// Quando o provedor nao envia um event id estavel, geramos hash do body
/// (<see cref="RawBodyHash"/>) e usamos como <see cref="EventIdExterno"/>
/// (lossy mas funcional).
/// </para>
/// </summary>
public class WebhookRecebido
{
    public Guid Id { get; set; }

    /// <summary>"EfiPix" | "EfiBoleto" | "Stripe" | etc.</summary>
    public string Provedor { get; set; } = null!;

    /// <summary>ID estavel do evento no provedor, ou hash do body se nao disponivel.</summary>
    public string EventIdExterno { get; set; } = null!;

    /// <summary>SHA-256 hex do body bruto — sempre presente para auditoria.</summary>
    public string RawBodyHash { get; set; } = null!;

    public DateTime RecebidoEm { get; set; }
    public DateTime? ProcessadoEm { get; set; }
    public bool Sucesso { get; set; }
    public string? Erro { get; set; }

    public static WebhookRecebido Criar(string provedor, string eventIdExterno, string rawBodyHash)
    {
        return new WebhookRecebido
        {
            Id = Guid.NewGuid(),
            Provedor = provedor,
            EventIdExterno = eventIdExterno,
            RawBodyHash = rawBodyHash,
            RecebidoEm = DateTime.UtcNow
        };
    }

    public void MarcarProcessado(bool sucesso, string? erro = null)
    {
        ProcessadoEm = DateTime.UtcNow;
        Sucesso = sucesso;
        Erro = erro;
    }
}
