using EasyStock.Application.Seguranca;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace EasyStock.Observabilidade;

/// <summary>
/// Redige o log OTLP antes do exporter (#2390), com o mesmo <see cref="SensitivePatterns"/> do <see cref="SpanQueRedige"/>.
/// Serve ao host que manda o <c>ILogger</c> direto ao OpenTelemetry, sem Serilog (o console Web): lá o
/// <c>SinkQueRedige</c> não existe e a mensagem, os atributos e a exceção iam crus ao OpenObserve.
///
/// <para>Registre com <c>AddProcessor</c> ANTES do <c>AddOtlpExporter</c>. Redige a mensagem formatada, o corpo e todo
/// atributo de texto; atributos numéricos e booleanos passam como estão. A mensagem da exceção não é editável: o
/// processador tira a exceção do registro e grava <c>exception.type</c>, <c>exception.message</c> e
/// <c>exception.stacktrace</c> redigidos, os mesmos nomes que o exporter OTLP usaria. Os escopos do <c>ILogger</c>
/// também não são editáveis: o host desliga <c>IncludeScopes</c> (o trace_id e o span_id vêm da Activity, não do escopo).</para>
/// </summary>
public sealed class LogQueRedige : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.FormattedMessage is { } mensagem)
            data.FormattedMessage = SensitivePatterns.Redact(mensagem);
        if (data.Body is { } corpo)
            data.Body = SensitivePatterns.Redact(corpo);

        List<KeyValuePair<string, object?>>? atributos = null;
        if (data.Attributes is { } originais)
        {
            for (var i = 0; i < originais.Count; i++)
            {
                if (originais[i].Value is not string texto) continue;
                var redigido = SensitivePatterns.Redact(texto);
                if (redigido == texto) continue;
                atributos ??= [.. originais];
                atributos[i] = new(originais[i].Key, redigido);
            }
        }

        if (data.Exception is { } excecao)
        {
            atributos ??= data.Attributes is null ? [] : [.. data.Attributes];
            atributos.Add(new("exception.type", excecao.GetType().FullName));
            atributos.Add(new("exception.message", SensitivePatterns.Redact(excecao.Message)));
            atributos.Add(new("exception.stacktrace", SensitivePatterns.Redact(excecao.ToString())));
            data.Exception = null;
        }

        if (atributos is not null)
            data.Attributes = atributos;
    }
}
