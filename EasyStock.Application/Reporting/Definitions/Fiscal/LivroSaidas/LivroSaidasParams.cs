namespace EasyStock.Application.Reporting.Definitions.Fiscal.LivroSaidas;

/// <summary>
/// Parâmetros do relatório "Livro de Saídas (NFC-e)".
/// Usa competência mensal (mês inteiro) — alinhado com apuração fiscal SEFAZ.
/// </summary>
public sealed record LivroSaidasParams(
    /// <summary>Primeiro dia do mês de competência.</summary>
    DateOnly De,
    /// <summary>Último dia do mês de competência (inclusive).</summary>
    DateOnly Ate);
