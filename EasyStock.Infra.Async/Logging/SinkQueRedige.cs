using System.Text.RegularExpressions;
using EasyStock.Application.Seguranca;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace EasyStock.Infra.Async.Logging;

/// <summary>
/// Envolve qualquer sink do Serilog e redige o evento antes de ele sair (I4.5, #1698; invariante 16). Vale
/// para console, arquivo e OTLP, na API e no Worker: antes só o arquivo da API era redigido, e o console é o
/// log do contêiner em produção.
///
/// <para>Redige o texto do template, o valor de toda propriedade (inclusive dentro de objeto, lista e
/// dicionário), o valor inteiro de propriedade com nome sensível (<c>Token</c>, <c>Senha</c>, <c>ApiKey</c>) e
/// a exceção. A exceção só é trocada quando a redação muda o texto dela; aí vira
/// <see cref="ExcecaoRedigida"/>, que guarda o tipo original.</para>
/// </summary>
public sealed partial class SinkQueRedige(ILogEventSink interno) : ILogEventSink, IDisposable
{
    private static readonly MessageTemplateParser Parser = new();

    [GeneratedRegex("(?i)(password|senha|secret|segredo|token|api[_-]?key|authorization|credencial|kek)")]
    private static partial Regex NomeSensivel();

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var texto = logEvent.MessageTemplate.Text;
        var redigido = SensitivePatterns.Redact(texto);
        var template = redigido == texto ? logEvent.MessageTemplate : Parser.Parse(redigido);
        var propriedades = logEvent.Properties.Select(p => new LogEventProperty(p.Key, Redigir(p.Key, p.Value)));

        interno.Emit(new LogEvent(
            logEvent.Timestamp, logEvent.Level, ExcecaoRedigida.De(logEvent.Exception), template, propriedades,
            logEvent.TraceId ?? default, logEvent.SpanId ?? default));
    }

    private static LogEventPropertyValue Redigir(string nome, LogEventPropertyValue valor)
    {
        if (NomeSensivel().IsMatch(nome) && valor is ScalarValue { Value: not null })
            return new ScalarValue(SensitivePatterns.RedactedToken);

        return valor switch
        {
            ScalarValue { Value: string s } => new ScalarValue(SensitivePatterns.Redact(s)),
            SequenceValue lista => new SequenceValue(lista.Elements.Select(e => Redigir(nome, e))),
            StructureValue objeto => new StructureValue(
                objeto.Properties.Select(p => new LogEventProperty(p.Name, Redigir(p.Name, p.Value))), objeto.TypeTag),
            DictionaryValue dicionario => new DictionaryValue(dicionario.Elements.Select(e =>
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(e.Key, Redigir(e.Key.Value?.ToString() ?? nome, e.Value)))),
            _ => valor,
        };
    }

    public void Dispose() => (interno as IDisposable)?.Dispose();
}

/// <summary>
/// Exceção com o texto já redigido. <see cref="ToString"/> é o da original, redigido, e por isso começa com o
/// tipo dela; <see cref="TipoOriginal"/> guarda o nome para o sink que só olha o tipo.
/// </summary>
public sealed class ExcecaoRedigida : Exception
{
    private readonly string _texto;

    private ExcecaoRedigida(string mensagem, string texto, string tipoOriginal, string? pilha) : base(mensagem)
    {
        _texto = texto;
        TipoOriginal = tipoOriginal;
        StackTrace = pilha;
    }

    public string TipoOriginal { get; }

    public override string? StackTrace { get; }

    public override string ToString() => _texto;

    /// <summary>A própria exceção quando não há o que redigir; senão, a versão redigida.</summary>
    public static Exception? De(Exception? original)
    {
        if (original is null or ExcecaoRedigida) return original;
        var texto = original.ToString();
        var redigido = SensitivePatterns.Redact(texto);
        return redigido == texto
            ? original
            : new ExcecaoRedigida(SensitivePatterns.Redact(original.Message), redigido, original.GetType().FullName ?? original.GetType().Name, original.StackTrace);
    }
}