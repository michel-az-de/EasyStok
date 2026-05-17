namespace EasyStock.Application.Reporting;

/// <summary>
/// Catálogo em memória de todas as definições de relatório registradas via DI.
/// </summary>
public sealed class ReportRegistry
{
    private readonly Dictionary<string, IReportDefinition> _definitions;

    public ReportRegistry(IEnumerable<IReportDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReportDefinition? Find(string key) =>
        _definitions.TryGetValue(key, out var d) ? d : null;

    public IReportDefinition Get(string key) =>
        _definitions.TryGetValue(key, out var d)
            ? d
            : throw new KeyNotFoundException($"Relatório '{key}' não encontrado no catálogo.");

    public IReadOnlyList<IReportDefinition> All() =>
        _definitions.Values.OrderBy(d => d.Categoria).ThenBy(d => d.Key).ToList();

    public bool Contains(string key) => _definitions.ContainsKey(key);
}
