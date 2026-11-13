using System.Diagnostics;
using EasyStock.Application.Seguranca;
using OpenTelemetry;

namespace EasyStock.Observabilidade;

/// <summary>
/// Redige o span antes do exporter (#2376), com o mesmo <see cref="SensitivePatterns"/> do <c>SinkQueRedige</c>.
/// Sem ele, o trace levava ao OpenObserve o caminho com telefone ou CPF, a URL das integrações que não estão no
/// <c>EnderecoForaDaTelemetria</c> (WAHA, Mercado Pago, e-mail, SMS) e o texto do SQL.
///
/// <para>Registre com <c>AddProcessor</c> ANTES do <c>AddOtlpExporter</c>: os processadores rodam na ordem do
/// registro. Redige o nome do span e toda tag de texto; tags numéricas e booleanas passam como estão. Eventos do
/// span ficam de fora: a instrumentação daqui não grava exceção (<c>RecordException</c> desligado).</para>
/// </summary>
public sealed class SpanQueRedige : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var nome = SensitivePatterns.Redact(data.DisplayName);
        if (nome != data.DisplayName)
            data.DisplayName = nome;

        List<(string Chave, string Valor)>? trocas = null;
        foreach (var (chave, valor) in data.TagObjects)
        {
            if (valor is not string texto) continue;
            var redigido = SensitivePatterns.Redact(texto);
            if (redigido != texto)
                (trocas ??= []).Add((chave, redigido));
        }

        // Fora do foreach: SetTag durante a enumeração de TagObjects invalida o enumerador.
        if (trocas is null) return;
        foreach (var (chave, valor) in trocas)
            data.SetTag(chave, valor);
    }
}
