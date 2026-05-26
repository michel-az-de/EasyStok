namespace EasyStock.Application.UseCases.Storefront.Aprovacao;

/// <summary>
/// Motivo canônico de recusa de pedido Storefront pela Babá (TASK-EZ-APROVAR-001).
///
/// <para>
/// Valor enviado no body do POST <c>/api/storefront/pedidos/{id}/recusar</c>.
/// Persistido na coluna <c>pedidos.motivo_recusa</c> (varchar 40) via
/// <see cref="MotivoRecusaExtensions.ToCanonicalString"/>.
/// </para>
/// </summary>
public enum MotivoRecusa
{
    /// <summary>Item indisponível na cozinha — promove conversa via WhatsApp.</summary>
    EstoqueInsuficiente = 1,

    /// <summary>
    /// Restrição operacional (capacidade, horário inviável, fora de zona) que
    /// não foi pega antes do checkout. Cliente precisa reagendar.
    /// </summary>
    Operacional = 2,

    /// <summary>Motivo livre — preencher <c>mensagemCliente</c> com explicação custom.</summary>
    Outro = 99,
}

/// <summary>Tradução bidirecional <see cref="MotivoRecusa"/> ↔ string canônica.</summary>
public static class MotivoRecusaExtensions
{
    public const string EstoqueInsuficiente = "estoque_insuficiente";
    public const string Operacional = "operacional";
    public const string Outro = "outro";

    public static string ToCanonicalString(this MotivoRecusa motivo) => motivo switch
    {
        MotivoRecusa.EstoqueInsuficiente => EstoqueInsuficiente,
        MotivoRecusa.Operacional => Operacional,
        MotivoRecusa.Outro => Outro,
        _ => throw new ArgumentOutOfRangeException(
            nameof(motivo), motivo, "MotivoRecusa fora do enum."),
    };

    /// <summary>
    /// Parse case-insensitive. Aceita tanto a string canônica
    /// (<c>"estoque_insuficiente"</c>) quanto o nome do enum (<c>"EstoqueInsuficiente"</c>)
    /// e a versão UPPER do contrato (<c>"ESTOQUE_INSUFICIENTE"</c>).
    /// Retorna <c>false</c> em null/empty/desconhecido — caller deve devolver 422.
    /// </summary>
    public static bool TryParse(string? raw, out MotivoRecusa motivo)
    {
        motivo = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        switch (raw.Trim().ToLowerInvariant())
        {
            case EstoqueInsuficiente:
            case "estoqueinsuficiente":
                motivo = MotivoRecusa.EstoqueInsuficiente;
                return true;
            case Operacional:
                motivo = MotivoRecusa.Operacional;
                return true;
            case Outro:
                motivo = MotivoRecusa.Outro;
                return true;
            default:
                return false;
        }
    }
}
