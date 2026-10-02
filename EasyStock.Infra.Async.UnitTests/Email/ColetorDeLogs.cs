using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>Logger que guarda tudo (mensagem formatada mais os valores estruturados) para o teste varrer.</summary>
internal sealed class ColetorDeLogs<T> : ILogger<T>
{
    private readonly List<(LogLevel Nivel, string Texto)> _linhas = [];
    private readonly object _trava = new();

    public IReadOnlyList<(LogLevel Nivel, string Texto)> Linhas
    {
        get
        {
            lock (_trava)
                return _linhas.ToArray();
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
            ? string.Join(";", pares.Select(p => $"{p.Key}={p.Value}"))
            : string.Empty;
        // A excecao entra no texto de proposito: se o servico a passar ao logger, o teste enxerga o vazamento.
        var texto = formatter(state, exception) + "|" + valores + "|" + exception;
        lock (_trava)
            _linhas.Add((logLevel, texto));
    }
}
