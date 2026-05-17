namespace EasyStock.Domain.Reporting;

/// <summary>
/// Metadados do esquema de saída de um relatório — colunas ordenadas,
/// título e nome do arquivo.
/// </summary>
public sealed class ReportSchema
{
    private readonly List<ReportColumn> _columns;

    public ReportSchema(string title, string fileNameBase, IEnumerable<ReportColumn> columns)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title é obrigatório.", nameof(title));
        if (string.IsNullOrWhiteSpace(fileNameBase))
            throw new ArgumentException("FileNameBase é obrigatório.", nameof(fileNameBase));

        Title        = title;
        FileNameBase = fileNameBase;
        _columns     = columns.OrderBy(c => c.Order).ToList();
    }

    /// <summary>Título exibido no header do PDF e como nome da aba do Excel.</summary>
    public string Title { get; }

    /// <summary>
    /// Base do nome do arquivo sem extensão (ex: "vendas-por-periodo_2026-05-01_a_2026-05-31").
    /// Caracteres especiais devem ser evitados — apenas a-z0-9-_.
    /// </summary>
    public string FileNameBase { get; }

    /// <summary>Colunas ordenadas por <see cref="ReportColumn.Order"/>.</summary>
    public IReadOnlyList<ReportColumn> Columns => _columns;
}
