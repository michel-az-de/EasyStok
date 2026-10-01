using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Impresso do pedido em HTML (S49): um modelo Scriban por papel, CSS da marca e CODE128 em SVG, numa
/// página autocontida com <c>@page</c> do tamanho do papel. Todo texto vem escapado daqui (o renderer roda sem
/// escape automático para o SVG entrar cru). Layout aprovado em
/// <c>docs/plan/atendimento-whatsapp/impressos/pedido-aprovado.html</c>.
/// </summary>
public sealed class PedidoImpressoHtml(IRendererTemplate renderer)
{
    public const string Etiqueta10x15 = "etiqueta-10x15";
    public const string Cupom58 = "cupom-58";
    public const string A4 = "a4";
    public const string CaminhoFontes = "/impressao/fontes";

    public static readonly IReadOnlyList<string> Modelos = [Etiqueta10x15, Cupom58, A4];

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);
    private static readonly string[] DiasSemana = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

    private static readonly IReadOnlyDictionary<string, string> TamanhoPagina = new Dictionary<string, string>
    {
        [Etiqueta10x15] = "100mm 150mm",
        [Cupom58] = "58mm auto",
        [A4] = "A4",
    };

    /// <param name="baseUrl">Origem da Api (ex.: <c>https://api.casadababa.com.br</c>), para as fontes.</param>
    public async Task<string> RenderizarAsync(PedidoImpressoDto pedido, string modelo, string baseUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (!TamanhoPagina.TryGetValue(modelo, out var pagina))
            throw new ArgumentException($"Modelo desconhecido: {modelo}.", nameof(modelo));

        var corpo = await renderer.RenderizarAsync(
            Recurso($"pedido.{modelo}.sbn"),
            new Dictionary<string, object?> { ["m"] = Modelo(pedido) },
            htmlEscape: false,
            ct);

        var fontes = Html.Encode(baseUrl.TrimEnd('/') + CaminhoFontes);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />");
        sb.Append("<title>Pedido #").Append(Html.Encode(pedido.Numero)).Append("</title><style>");
        sb.Append("@page{size:").Append(pagina).Append(";margin:0}");
        sb.Append("@font-face{font-family:\"Lora\";src:url(\"").Append(fontes).Append("/Lora.ttf\") format(\"truetype\");font-weight:400 700;font-style:normal}");
        sb.Append("@font-face{font-family:\"Nunito Sans\";src:url(\"").Append(fontes).Append("/NunitoSans.ttf\") format(\"truetype\");font-weight:200 1000;font-style:normal}");
        sb.Append(Recurso("impressos.css"));
        sb.Append("</style></head><body>").Append(corpo).Append("</body></html>");
        return sb.ToString();
    }

    private static Dictionary<string, object?> Modelo(PedidoImpressoDto p)
    {
        var hoje = DateOnly.FromDateTime(p.ImpressoEm);
        var prazo = p.Prazo;
        return new Dictionary<string, object?>
        {
            ["numero"] = T(p.Numero),
            ["casa"] = new Dictionary<string, object?>
            {
                ["nome"] = T(p.Casa.Nome),
                ["logo_url"] = T(p.Casa.LogoUrl),
                ["contatos"] = new[]
                    {
                        p.Casa.Documento is { } d ? Documento(d) : null,
                        p.Casa.Site is { } s ? Site(s) : null,
                        p.Casa.WhatsApp is { } w ? "WhatsApp " + Telefone(w) : null,
                    }
                    .Where(c => c is not null)
                    .Select(T)
                    .ToList(),
            },
            ["prazo"] = new Dictionary<string, object?>
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
            },
            ["solicitado"] = T(DataCurta(p.SolicitadoEm, hoje) + " " + Hora(p.SolicitadoEm)),
            ["solicitado_longo"] = T(DataLonga(p.SolicitadoEm, hoje) + " " + Hora(p.SolicitadoEm)),
            ["entrega"] = T(Entrega(p.Entrega)),
            ["cliente"] = new Dictionary<string, object?>
            {
                ["id"] = T(p.Cliente.IdCurto),
                ["nome"] = T(p.Cliente.Nome ?? "Cliente sem nome"),
                ["telefone"] = p.Cliente.Telefone is { } tel ? T(Telefone(tel)) : null,
                ["endereco"] = T(p.Cliente.Endereco),
            },
            ["itens"] = p.Itens.Select(i => new Dictionary<string, object?>
            {
                ["quantidade"] = Quantidade(i.Quantidade),
                ["nome"] = T(i.Nome),
                ["observacao"] = T(i.Observacao),
                ["unitario"] = Moeda(i.Unitario),
                ["subtotal"] = Moeda(i.Subtotal),
            }).ToList(),
            ["quantidade_total"] = Quantidade(p.QuantidadeTotal) + (p.QuantidadeTotal == 1 ? " item" : " itens"),
            ["observacao"] = T(p.Observacao),
            ["nota"] = T(p.Nota),
            ["cobranca"] = new Dictionary<string, object?>
            {
                ["total"] = Moeda(p.Cobranca.Total),
                ["rotulo"] = T((p.Cobranca.Pago ? "Pago" : "Cobrar na entrega")
                               + (p.Cobranca.Forma is { } f ? " · " + Forma(f) : string.Empty)),
            },
            ["barras"] = Code128Svg.Gerar(p.Numero),
            ["alterado"] = Curta(p.AlteradoEm),
            ["impresso"] = Curta(p.ImpressoEm),
            ["alterado_longo"] = T(DataLonga(p.AlteradoEm, null) + " " + Hora(p.AlteradoEm)),
            ["impresso_longo"] = T(DataLonga(p.ImpressoEm, null) + " " + Hora(p.ImpressoEm)),
        };
    }

    private static string? T(string? s) => s is null ? null : Html.Encode(s);

    private static string Hora(DateTime d) => d.ToString("HH:mm", PtBr);

    private static string Curta(DateTime d) => d.ToString("dd/MM HH:mm", PtBr);

    private static string DataCurta(DateTime d, DateOnly hoje) =>
        DateOnly.FromDateTime(d) == hoje ? "hoje" : d.ToString("dd/MM", PtBr);

    private static string DataLonga(DateTime d, DateOnly? hoje) =>
        hoje is { } h && DateOnly.FromDateTime(d) == h ? "hoje" : DiasSemana[(int)d.DayOfWeek] + " " + d.ToString("dd/MM", PtBr);

    /// <summary>"10–11h" quando a janela é em horas cheias; senão "10:00–11:00". Sem fim, só o início.</summary>
    private static string JanelaCurta(DateTime inicio, DateTime? fim)
    {
        if (fim is not { } f) return Hora(inicio);
        return inicio.Minute == 0 && f.Minute == 0
            ? $"{inicio.Hour}–{f.Hour}h"
            : Hora(inicio) + "–" + Hora(f);
    }

    private static string Moeda(decimal v) => "R$ " + v.ToString("N2", PtBr);

    private static string Quantidade(decimal q) =>
        q == decimal.Truncate(q) ? q.ToString("0", PtBr) : q.ToString("0.###", PtBr);

    private static string Entrega(PedidoImpressoEntregaDto? e)
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

    private static string Forma(string forma) => forma.Trim().ToLowerInvariant() switch
    {
        "pix" => "PIX",
        "cartao" or "cartão" => "Cartão",
        "credito" or "crédito" or "cartao_credito" => "Crédito",
        "debito" or "débito" or "cartao_debito" => "Débito",
        "dinheiro" => "Dinheiro",
        var outra => char.ToUpper(outra[0], PtBr) + outra[1..],
    };

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

    private static string Documento(string documento)
    {
        var d = new string(documento.Where(char.IsDigit).ToArray());
        return d.Length switch
        {
            14 => $"CNPJ {d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            11 => $"CPF {d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            _ => documento.Trim(),
        };
    }

    private static string Site(string site) =>
        site.Trim().Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/');

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Recursos = new();

    private static string Recurso(string arquivo) => Recursos.GetOrAdd(arquivo, nome =>
    {
        var logico = "Impressao." + nome;
        using var stream = typeof(PedidoImpressoHtml).Assembly.GetManifestResourceStream(logico)
            ?? throw new InvalidOperationException($"Recurso embutido '{logico}' não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });
}
