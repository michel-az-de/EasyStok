using System.Globalization;

namespace EasyStock.Domain.Reporting;

/// <summary>
/// Descreve uma coluna de saída de um relatório — usada pelo exporter para
/// ordenar colunas, formatar valores e gerar cabeçalhos.
/// </summary>
public sealed class ReportColumn
{
    public ReportColumn(
        string propertyName,
        string headerLabel,
        int order,
        string? formatString = null,
        CultureInfo? cultureOverride = null)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("PropertyName é obrigatório.", nameof(propertyName));
        if (string.IsNullOrWhiteSpace(headerLabel))
            throw new ArgumentException("HeaderLabel é obrigatório.", nameof(headerLabel));

        PropertyName = propertyName;
        HeaderLabel  = headerLabel;
        Order        = order;
        FormatString = formatString;
        CultureOverride = cultureOverride;
    }

    /// <summary>Nome da propriedade no TRow (case-sensitive).</summary>
    public string PropertyName { get; }

    /// <summary>Texto do cabeçalho na planilha / CSV.</summary>
    public string HeaderLabel { get; }

    /// <summary>Índice de ordenação (0-based).</summary>
    public int Order { get; }

    /// <summary>Formato opcional aplicado via TypeConverterOption.Format.</summary>
    public string? FormatString { get; }

    /// <summary>Culture usada para formatar este campo específico (sobrepõe o global).</summary>
    public CultureInfo? CultureOverride { get; }
}
