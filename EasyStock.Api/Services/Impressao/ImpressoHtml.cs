using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Peças comuns dos impressos da Casa da Baba (S49, S52, #1632): página autocontida com <c>@page</c> do tamanho do
/// papel, fontes da marca servidas pela Api, CSS e modelos embutidos, e a formatação de texto (datas, horas,
/// moeda, telefone, prazo). Todo texto sai escapado por <see cref="T"/>: os modelos rodam sem escape automático
/// para o SVG do QR entrar cru.
/// </summary>
public static class ImpressoHtml
{
    public const string Etiqueta10x15 = "etiqueta-10x15";
    public const string Cupom80 = "cupom-80";
    public const string Cupom58 = "cupom-58";
    public const string A4 = "a4";
    public const string A5 = "a5";
    public const string CaminhoFontes = "/impressao/fontes";

    /// <summary>Ornamento do fecho: filete, losango, filete. Um traço só, para sair igual em 1 bit.</summary>
    public const string Ornamento =
        "<svg class=\"orn\" viewBox=\"0 0 120 8\" aria-hidden=\"true\"><path d=\"M0 4H48M72 4H120\" stroke=\"currentColor\" stroke-width=\"1.2\" fill=\"none\"/>"
        + "<path d=\"M60 .6 64.6 4 60 7.4 55.4 4z\" fill=\"currentColor\"/><circle cx=\"51.5\" cy=\"4\" r=\"1\" fill=\"currentColor\"/>"
        + "<circle cx=\"68.5\" cy=\"4\" r=\"1\" fill=\"currentColor\"/></svg>";

    /// <summary>Papéis aceitos por todos os documentos do layout "bilhete da casa" (#1632).</summary>
    public static readonly IReadOnlyList<string> Papeis = [A4, A5, Etiqueta10x15, Cupom80, Cupom58];

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);
    private static readonly string[] DiasSemana = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];
    private static readonly string[] DiasSemanaLongos = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];
    private static readonly ConcurrentDictionary<string, string> Recursos = new();

    private static readonly IReadOnlyDictionary<string, string> TamanhoPagina = new Dictionary<string, string>
    {
        [Etiqueta10x15] = "100mm 150mm",
        [Cupom80] = "80mm auto",
        [Cupom58] = "58mm auto",
        [A4] = "A4",
        [A5] = "A5",
    };

    private static readonly IReadOnlyDictionary<string, string> ClassePapel = new Dictionary<string, string>
    {
        [Etiqueta10x15] = "s-1015",
        [Cupom80] = "s-80",
        [Cupom58] = "s-58",
        [A4] = "s-a4",
        [A5] = "s-a5",
    };

    /// <summary>Documento HTML completo em volta do <paramref name="corpo"/> já renderizado.</summary>
    /// <param name="baseUrl">Origem da Api (ex.: <c>https://api.casadababa.com.br</c>), para as fontes.</param>
    public static string Pagina(string titulo, string modelo, string baseUrl, string corpo)
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
        sb.Append(Recurso("bilhete.css"));
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

    /// <summary>Jato de tinta (A4, A5): cor e duas colunas. O resto é térmica: só preto, uma coluna.</summary>
    public static bool JatoDeTinta(string modelo) => modelo is A4 or A5;

    /// <summary>Papel para os modelos: classes CSS do <c>article</c> e os desvios de estrutura.</summary>
    public static Dictionary<string, object?> Papel(string modelo, string documento) => new()
    {
        ["classes"] = $"{ClassePapel[modelo]} {(JatoDeTinta(modelo) ? "tinta" : "termica")} d-{documento}",
        ["largo"] = JatoDeTinta(modelo),
        ["etiqueta"] = modelo == Etiqueta10x15,
    };

    /// <summary>Logo só entra onde sai bem: sempre no jato de tinta; na térmica, só se a loja pedir.</summary>
    /// <remarks>Caminho relativo (storage local) vira absoluto: o console imprime o HTML em outra origem.</remarks>
    public static string? Logo(string? logoUrl, string modelo, bool logoNaTermica, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl) || (!JatoDeTinta(modelo) && !logoNaTermica)) return null;
        var url = logoUrl.Trim();
        return T(url.StartsWith('/') ? baseUrl.TrimEnd('/') + url : url);
    }

    /// <summary>Prazo para os modelos, igual em todos os documentos.</summary>
    public static Dictionary<string, object?> Prazo(PedidoImpressoPrazoDto prazo, DateOnly hoje) => new()
    {
        ["agendado"] = prazo.Agendado,
        ["selo"] = prazo.Agendado ? "AGENDADO" : "IMEDIATO",
        ["data"] = prazo.Agendado ? prazo.Entrega.ToString("dd/MM", PtBr) : DataCurta(prazo.Entrega, hoje),
        ["data_longa"] = DataLonga(prazo.Entrega, prazo.Agendado ? null : hoje),
        ["dia_longo"] = !prazo.Agendado && DateOnly.FromDateTime(prazo.Entrega) == hoje ? "hoje" : DiaLongo(prazo.Entrega),
        ["hora_curta"] = prazo.Agendado ? JanelaCurta(prazo.Entrega, prazo.EntregaFim) : "~" + Hora(prazo.Entrega),
        ["janela_longa"] = prazo.Agendado ? JanelaLonga(prazo.Entrega, prazo.EntregaFim) : "por volta das " + Hora(prazo.Entrega),
        ["pronto_ate"] = Hora(prazo.ProntoAte),
    };

    public static string? T(string? s) => s is null ? null : Html.Encode(s);

    public static string Hora(DateTime d) => d.ToString("HH:mm", PtBr);

    public static string Curta(DateTime d) => d.ToString("dd/MM HH:mm", PtBr);

    public static string DataCurta(DateTime d, DateOnly hoje) =>
        DateOnly.FromDateTime(d) == hoje ? "hoje" : d.ToString("dd/MM", PtBr);

    public static string DataLonga(DateTime d, DateOnly? hoje) =>
        hoje is { } h && DateOnly.FromDateTime(d) == h ? "hoje" : DiasSemana[(int)d.DayOfWeek] + " " + d.ToString("dd/MM", PtBr);

    /// <summary>"sexta, 16/10".</summary>
    public static string DiaLongo(DateTime d) => DiasSemanaLongos[(int)d.DayOfWeek] + ", " + d.ToString("dd/MM", PtBr);

    /// <summary>"10–11h" quando a janela é em horas cheias; senão "10:00–11:00". Sem fim, só o início.</summary>
    public static string JanelaCurta(DateTime inicio, DateTime? fim)
    {
        if (fim is not { } f) return Hora(inicio);
        return inicio.Minute == 0 && f.Minute == 0
            ? $"{inicio.Hour}–{f.Hour}h"
            : Hora(inicio) + "–" + Hora(f);
    }

    /// <summary>"entre 10h e 11h" (ou "entre 10:30 e 11:30"); sem fim, "às 10h".</summary>
    public static string JanelaLonga(DateTime inicio, DateTime? fim)
    {
        static string H(DateTime d) => d.Minute == 0 ? d.Hour + "h" : d.ToString("HH:mm", PtBr);
        return fim is { } f ? $"entre {H(inicio)} e {H(f)}" : "às " + H(inicio);
    }

    public static string Moeda(decimal v) => "R$ " + v.ToString("N2", PtBr);

    /// <summary>Valor sem o "R$", para a coluna de preços da lista.</summary>
    public static string Valor(decimal v) => v.ToString("N2", PtBr);

    public static string Quantidade(decimal q) =>
        q == decimal.Truncate(q) ? q.ToString("0", PtBr) : q.ToString("0.###", PtBr);

    /// <summary>"2" para unidade; "1,5 kg" quando a unidade é de medida.</summary>
    public static string QuantidadeComUnidade(decimal q, string unidade) =>
        unidade == MontarPedidoImpressoUseCase.UnidadePadrao ? Quantidade(q) : Quantidade(q) + " " + unidade;

    /// <summary>Nulo sem nome: o Scriban trata string vazia como verdadeira e a saudação sairia ", vamos à mesa?".</summary>
    public static string? PrimeiroNome(string? nome) =>
        string.IsNullOrWhiteSpace(nome) ? null : nome.Trim().Split(' ', 2)[0];

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

    /// <summary>Link do WhatsApp da casa já com o pedido no texto; nulo sem número utilizável.</summary>
    public static string? LinkWhatsApp(string? whatsApp, string numeroPedido)
    {
        if (string.IsNullOrWhiteSpace(whatsApp)) return null;
        var d = new string(whatsApp.Where(char.IsDigit).ToArray());
        if (d.Length is 10 or 11) d = "55" + d;
        return d.Length is 12 or 13 ? $"https://wa.me/{d}?text=Pedido%20{Uri.EscapeDataString(numeroPedido)}" : null;
    }
}
