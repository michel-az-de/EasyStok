using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace EasyStock.Api.Observability.Logging;

/// <summary>
/// ITextFormatter que renderiza o LogEvent com o template padrao e entao aplica
/// <see cref="SensitivePatterns.Redact"/> no resultado completo antes de escrever no sink.
///
/// **Cobre todos os campos do output** — nao so MessageTemplate. Em particular:
/// - {Message} (template renderizado com properties expandidas)
/// - {Properties:j} (todas as properties serializadas como JSON)
/// - {Exception} (Exception.ToString() inteiro — onde DbUpdateException coloca conn string)
///
/// Isso e o que torna o redactor write-time efetivo: o sink File serializa Exception
/// no output template ({Exception}), e nessa serializacao a conn string aparece em
/// texto plano. Custom ILogEventEnricher nao alcanca isso porque LogEvent.Exception
/// e set-once e imutavel. Custom TextFormatter alcanca porque opera no resultado final.
///
/// Reusa <see cref="MessageTemplateTextFormatter"/> internamente — nao re-implementa
/// renderizacao de template Serilog, so envolve com redaction layer.
/// </summary>
public sealed class RedactingTextFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _inner;

    public RedactingTextFormatter(string outputTemplate)
    {
        _inner = new MessageTemplateTextFormatter(outputTemplate, formatProvider: null);
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        // Renderiza no buffer local primeiro — precisamos do output completo para
        // aplicar regex em Exception.ToString() (que so o template expande).
        using var buffer = new StringWriter();
        _inner.Format(logEvent, buffer);
        var rendered = buffer.ToString();
        var redacted = SensitivePatterns.Redact(rendered);
        output.Write(redacted);
    }
}
