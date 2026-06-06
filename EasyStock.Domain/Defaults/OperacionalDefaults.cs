namespace EasyStock.Domain.Defaults;

/// <summary>
/// Compile-time defaults for operational settings shared across domain,
/// application, and infrastructure layers.
/// </summary>
public static class OperacionalDefaults
{
    public const int    DiasAlertaValidade = 15;
    public const int    DiasAlertaParado   = 30;

    /// <summary>
    /// Janela (em dias) da "vitrine de vencimento" operacional: card de vencimento
    /// do Dashboard, filtro "Vencendo" do Estoque e o realce vermelho da coluna
    /// Validade compartilham este valor para que contagem e lista sempre batam.
    /// Distinto de <see cref="DiasAlertaValidade"/> (15), que rege as notificações
    /// do sino — mantidos separados de propósito: a vitrine destaca o mais urgente,
    /// o sino alerta com mais antecedência.
    /// </summary>
    public const int    DiasVencimentoProximo = 7;
    public const int    QuantidadeMinima   = 5;
    public const int    QuantidadeCritica  = 2;
    public const string Moeda              = "BRL";
    public const string Timezone           = "America/Sao_Paulo";

    /// <summary>
    /// Multiplicador aplicado sobre o custo unitário quando o produto não tem PrecoVendaSugerido definido.
    /// Usado em queries de analytics e relatórios. Centralizado aqui para evitar drift entre repositórios.
    /// </summary>
    public const decimal FallbackMargemPrecoSugerido = 1.3m;
}
