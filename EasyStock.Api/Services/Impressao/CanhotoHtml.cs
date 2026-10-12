using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Canhoto em HTML para a bobina de 80 mm (S20): página autocontida (CSS embutido, copiado de
/// <c>EasyStock.Web/wwwroot/css/recibo.css</c>) para a aba do console imprimir pelo navegador.
/// </summary>
public static class CanhotoHtml
{
    private const string RecursoCss = "Impressao.canhoto.css";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Lazy<string> Css = new(CarregarCss);

    public static string Renderizar(CanhotoDto canhoto)
    {
        ArgumentNullException.ThrowIfNull(canhoto);
        var h = HtmlEncoder.Default;
        var c = canhoto.Cabecalho;
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.Append("<title>Canhoto #").Append(h.Encode(c.Numero)).Append("</title>");
        sb.Append("<style>").Append(Css.Value).Append("</style></head>");
        sb.Append("<body class=\"recibo-page\">");
        sb.Append("<div class=\"recibo-toolbar\"><button type=\"button\" onclick=\"window.print()\">Imprimir</button></div>");
        sb.Append("<div class=\"recibo\">");

        sb.Append("<div class=\"header\"><div class=\"empresa\">").Append(h.Encode(c.NomeCasa)).Append("</div>");
        sb.Append("<div class=\"id\">Pedido #").Append(h.Encode(c.Numero)).Append("</div></div>");

        sb.Append("<div class=\"meta\">");
        Meta(sb, h, "Cliente", c.Cliente);
        Meta(sb, h, "Telefone", c.Telefone);
        Meta(sb, h, "Endereço", c.Endereco);
        Meta(sb, h, "Entrega", c.AgendadoPara is { } a ? a.ToString("dd/MM HH:mm", PtBr) : "para já");
        Meta(sb, h, "Pago em", c.PagoEm?.ToString("dd/MM HH:mm", PtBr));
        sb.Append("</div>");

        foreach (var g in canhoto.Grupos)
        {
            sb.Append("<div class=\"grupo\"><h3>").Append(h.Encode(g.Titulo)).Append("</h3>");
            foreach (var i in g.Itens)
            {
                sb.Append("<div class=\"item\"><div class=\"nome\">")
                  .Append(h.Encode(CanhotoTexto.Quantidade(i.Quantidade))).Append("x ").Append(h.Encode(i.Nome));
                if (i.Porcao is not null) sb.Append(" (").Append(h.Encode(i.Porcao)).Append(')');
                sb.Append("</div>");
                if (i.Molho is not null) sb.Append("<div class=\"detalhe\">molho: ").Append(h.Encode(i.Molho)).Append("</div>");
                if (i.Observacao is not null) sb.Append("<div class=\"detalhe\">obs: ").Append(h.Encode(i.Observacao)).Append("</div>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        if (canhoto.Observacoes is not null)
            sb.Append("<div class=\"observacoes\">").Append(h.Encode(canhoto.Observacoes)).Append("</div>");

        sb.Append("<div class=\"footer\">").Append(h.Encode(canhoto.Rodape)).Append("</div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void Meta(StringBuilder sb, HtmlEncoder h, string rotulo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        sb.Append("<div class=\"label\">").Append(h.Encode(rotulo)).Append(":</div><div>").Append(h.Encode(valor)).Append("</div>");
    }

    private static string CarregarCss()
    {
        using var stream = typeof(CanhotoHtml).Assembly.GetManifestResourceStream(RecursoCss)
            ?? throw new InvalidOperationException($"Recurso embutido '{RecursoCss}' não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
