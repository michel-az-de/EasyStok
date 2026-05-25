namespace EasyStock.Domain.Defaults;

/// <summary>
/// Compile-time defaults for operational settings shared across domain,
/// application, and infrastructure layers.
/// </summary>
public static class OperacionalDefaults
{
    public const int DiasAlertaValidade = 15;
    public const int DiasAlertaParado = 30;
    public const int QuantidadeMinima = 5;
    public const int QuantidadeCritica = 2;
    public const string Moeda = "BRL";
    public const string Timezone = "America/Sao_Paulo";

    /// <summary>
    /// Multiplicador aplicado sobre o custo unitário quando o produto não tem PrecoVendaSugerido definido.
    /// Usado em queries de analytics e relatórios. Centralizado aqui para evitar drift entre repositórios.
    /// </summary>
    public const decimal FallbackMargemPrecoSugerido = 1.3m;
}
