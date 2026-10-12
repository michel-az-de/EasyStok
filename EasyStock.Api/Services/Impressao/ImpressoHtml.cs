using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Peças comuns dos impressos da Casa da Baba (S49, S52): página autocontida com <c>@page</c> do tamanho do
/// papel, fontes da marca servidas pela Api, CSS e modelos embutidos, e a formatação de texto (datas, horas,
/// moeda, telefone, prazo). Todo texto sai escapado por <see cref="T"/>: os modelos rodam sem escape automático
/// para o SVG do código de barras entrar cru.
/// </summary>
public static class ImpressoHtml
{
    public const string Etiqueta10x15 = "etiqueta-10x15";
    public const string Cupom58 = "cupom-58";
    public const string A4 = "a4";
    public const string CaminhoFontes = "/impressao/fontes";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);
    private static readonly string[] DiasSemana = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];
    private static readonly ConcurrentDictionary<string, string> Recursos = new();

    private static readonly IReadOnlyDictionary<string, string> TamanhoPagina = new Dictionary<string, string>
    {
        [Etiqueta10x15] = "100mm 150mm",
        [Cupom58] = "58mm auto",
        [A4] = "A4",
    };

    /// <summary>Documento HTML completo em volta do <paramref name="corpo"/> já renderizado.</summary>
    /// <param name="baseUrl">Origem da Api (ex.: <c>https://api.casadababa.com.br</c>), para as fontes.</param>
    /// <param name="cssExtra">Recursos CSS embutidos depois de <c>impressos.css</c> (ex.: <c>comanda.css</c>).</param>
    public static string Pagina(string titulo, string modelo, string baseUrl, string corpo, params string[] cssExtra)
    {
        if (!TamanhoPagina.TryGetValue(modelo, out var pagina))
            throw new ArgumentException($"Modelo desconhecido: {modelo}.", nameof(modelo));

        var fontes = Html.Encode(baseUrl.TrimEnd('/') + CaminhoFontes);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />");
        sb.Append("<title>").Append(Html.Encode(titulo)).Append("</title><style>");
        sb.Append("@page{size:").Append(pagina).Append(";margin:0}");
        sb.Append("@font-face{font-family:\"Lora\";src:url(\"").Append(fontes).Append("/Lora.ttf\") format(\"truetype\");font-weight:400 700;font-style:normal}");
        sb.Append("@font-face{font-family:\"Nunito Sans\";src:url(\"").Append(fontes).Append("/NunitoSans.ttf\") format(\"truetype\");font-weight:200 1000;font-style:normal}");
        sb.Append(Recurso("impressos.css"));
        foreach (var css in cssExtra) sb.Append(Recurso(css));
        sb.Append("</style></head><body>").Append(corpo).Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Recurso embutido <c>Impressao.{arquivo}</c> (modelo .sbn ou CSS), lido uma vez.</summary>
    public static string Recurso(string arquivo) => Recursos.GetOrAdd(arquivo, nome =>
    {
        var logico = "Impressao." + nome;
        using var stream = typeof(ImpressoHtml).Assembly.GetManifestResourceStream(logico)
            ?? throw new InvalidOperationException($"Recurso embutido '{logico}' não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    /// <summary>Prazo para os modelos, igual no Pedido e na Comanda.</summary>
    public static Dictionary<string, object?> Prazo(PedidoImpressoPrazoDto prazo, DateOnly hoje) => new()
    {
        ["selo"] = prazo.Agendado ? "AGENDADO" : "IMEDIATO",
        ["rotulo_data"] = prazo.Agendado ? "Entrega" : "Previsão",
        ["rotulo_hora"] = prazo.Agendado ? "Janela" : "Sai às",
        ["data"] = prazo.Agendado ? prazo.Entrega.ToString("dd/MM", PtBr) : DataCurta(prazo.Entrega, hoje),
        ["data_longa"] = DataLonga(prazo.Entrega, prazo.Agendado ? null : hoje),
        ["hora_curta"] = prazo.Agendado ? JanelaCurta(prazo.Entrega, prazo.EntregaFim) : "~" + Hora(prazo.Entrega),
        ["hora_longa"] = prazo.Agendado
            ? Hora(prazo.Entrega) + (prazo.EntregaFim is { } fim ? "–" + Hora(fim) : string.Empty)
            : "~" + Hora(prazo.Entrega),
        ["pronto_ate"] = Hora(prazo.ProntoAte),
    };

    public static string? T(string? s) => s is null ? null : Html.Encode(s);

    public static string Hora(DateTime d) => d.ToString("HH:mm", PtBr);

    public static string Curta(DateTime d) => d.ToString("dd/MM HH:mm", PtBr);

    public static string DataCurta(DateTime d, DateOnly hoje) =>
        DateOnly.FromDateTime(d) == hoje ? "hoje" : d.ToString("dd/MM", PtBr);

    public static string DataLonga(DateTime d, DateOnly? hoje) =>
        hoje is { } h && DateOnly.FromDateTime(d) == h ? "hoje" : DiasSemana[(int)d.DayOfWeek] + " " + d.ToString("dd/MM", PtBr);

    /// <summary>"10–11h" quando a janela é em horas cheias; senão "10:00–11:00". Sem fim, só o início.</summary>
    public static string JanelaCurta(DateTime inicio, DateTime? fim)
    {
        if (fim is not { } f) return Hora(inicio);
        return inicio.Minute == 0 && f.Minute == 0
            ? $"{inicio.Hour}–{f.Hour}h"
            : Hora(inicio) + "–" + Hora(f);
    }

    public static string Moeda(decimal v) => "R$ " + v.ToString("N2", PtBr);

    public static string Quantidade(decimal q) =>
        q == decimal.Truncate(q) ? q.ToString("0", PtBr) : q.ToString("0.###", PtBr);

    public static string Entrega(PedidoImpressoEntregaDto? e)
    {
        if (e is null) return "a definir";
        var tipo = e.Tipo switch
        {
            TipoEntregador.Motoboy => "Motoboy",
            TipoEntregador.Plataforma => "Plataforma",
            TipoEntregador.Proprio => "Entrega própria",
            _ => null,
        };
        var partes = new[] { tipo, e.Responsavel }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return partes.Count == 0 ? "a definir" : string.Join(" · ", partes);
    }

    /// <summary>(11) 98765-4412; com DDI 55 ou fixo de 8 dígitos também. Fora do padrão, como veio.</summary>
    public static string Telefone(string telefone)
    {
        var d = new string(telefone.Where(char.IsDigit).ToArray());
        if (d.Length is 12 or 13 && d.StartsWith("55", StringComparison.Ordinal)) d = d[2..];
        return d.Length switch
        {
            11 => $"({d[..2]}) {d[2..7]}-{d[7..]}",
            10 => $"({d[..2]}) {d[2..6]}-{d[6..]}",
            _ => telefone.Trim(),
        };
    }
}
