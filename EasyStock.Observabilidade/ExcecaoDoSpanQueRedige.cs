using System.Diagnostics;
using EasyStock.Application.Seguranca;

namespace EasyStock.Observabilidade;

/// <summary>
/// Redige a exceção que a fonte grava no span (#2440). O Npgsql chama <c>Activity.AddException</c> e o evento levava
/// <c>exception.message</c> e <c>exception.stacktrace</c> crus ao OpenObserve; o <c>RecordException</c> desligado só
/// vale para o AspNetCore e o HttpClient, e o <see cref="SpanQueRedige"/> não enxerga os eventos (são imutáveis).
///
/// <para>Usa o <see cref="ActivityListener.ExceptionRecorder"/> (.NET 9+): o <c>AddException</c> só preenche os campos
/// padrão das chaves que o gravador não pôs. O listener não tem <c>Sample</c>, então não vota na amostragem nem liga
/// span sozinho; quem decide continua sendo o OpenTelemetry.</para>
/// </summary>
public static class ExcecaoDoSpanQueRedige
{
    private static readonly object Trava = new();
    private static ActivityListener? _listener;

    /// <summary>Instala uma vez por processo; chamadas seguintes são ignoradas.</summary>
    public static void Instalar(params string[] fontes)
    {
        var alvo = new HashSet<string>(fontes, StringComparer.Ordinal);
        lock (Trava)
        {
            if (_listener is not null) return;
            _listener = new ActivityListener
            {
                ShouldListenTo = fonte => alvo.Contains(fonte.Name),
                ExceptionRecorder = Gravar,
            };
            ActivitySource.AddActivityListener(_listener);
        }
    }

    private static void Gravar(Activity atividade, Exception excecao, ref TagList tags)
    {
        tags.Add("exception.type", excecao.GetType().FullName);
        tags.Add("exception.message", SensitivePatterns.Redact(excecao.Message));
        tags.Add("exception.stacktrace", SensitivePatterns.Redact(excecao.ToString()));
    }
}
