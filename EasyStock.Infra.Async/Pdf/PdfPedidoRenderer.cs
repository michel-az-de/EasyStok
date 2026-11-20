using System.Globalization;
using System.Text;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Notifications.Anexos;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// #2962 (E3): o PDF do pedido (com o QR Pix) e o comprovante "Pago", feitos para a tela do celular: página de 110 mm de
/// largura e altura contínua (cabe em 390 px sem zoom), fonte grande e alto contraste, na marca (<see cref="MarcaDaCasa"/>).
/// O QR sai vetorial a partir da matriz do <see cref="IQrCodeEncoder"/>, com a zona de silêncio que o app do banco exige.
/// </summary>
public sealed class PdfPedidoRenderer(IQrCodeEncoder qr) : IRenderizadorPdfPedido
{
    private const float LarguraMm = 110;
    private const float QrMm = 70;
    private const int ZonaDeSilencio = 4;

    private static readonly CultureInfo PtBr = new("pt-BR");

    static PdfPedidoRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        FontesDaMarca.Registrar();
    }

    public Task<AnexoGerado> RenderizarPedidoAsync(ResumoPedidoDto resumo, PixPdfDto pix, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        ArgumentNullException.ThrowIfNull(pix);
        ct.ThrowIfCancellationRequested();
        var svg = SvgDoQr(qr.Matriz(pix.CopiaCola));
        return Task.FromResult(Pdf(page => Desenhar(page, resumo, pix, svg, pagoEm: null, comprovante: false)));
    }

    public Task<AnexoGerado> RenderizarComprovanteAsync(ResumoPedidoDto resumo, DateTime? pagoEm, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Pdf(page => Desenhar(page, resumo, pix: null, svgQr: null, pagoEm, comprovante: true)));
    }

    private static AnexoGerado Pdf(Action<QuestPDF.Fluent.PageDescriptor> pagina) =>
        new(Document.Create(d => d.Page(pagina)).GeneratePdf(), "application/pdf", "pdf");

    private static void Desenhar(
        QuestPDF.Fluent.PageDescriptor page, ResumoPedidoDto r, PixPdfDto? pix, string? svgQr, DateTime? pagoEm, bool comprovante)
    {
        page.ContinuousSize(LarguraMm, Unit.Millimetre);
        page.Margin(7, Unit.Millimetre);
        page.PageColor(MarcaDaCasa.Papel);
        page.DefaultTextStyle(t => t.FontFamily(MarcaDaCasa.FonteCorpo).FontSize(13).FontColor(MarcaDaCasa.Cacau));

        page.Content().Column(col =>
        {
            col.Spacing(4);
            if (!string.IsNullOrWhiteSpace(r.NomeLoja))
                col.Item().AlignCenter().Text(r.NomeLoja).FontFamily(MarcaDaCasa.FonteTitulo).FontSize(22).Bold().FontColor(MarcaDaCasa.Marca);
            col.Item().AlignCenter().Text(comprovante ? $"Comprovante do pedido {r.Codigo}" : $"Pedido {r.Codigo}")
                .FontSize(15).SemiBold().FontColor(MarcaDaCasa.CacauSuave);
            if (comprovante) Selo(col, pagoEm);
            Divisor(col);

            foreach (var item in r.Itens)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text($"{item.Quantidade.ToString("0.###", PtBr)} × {item.Nome}").SemiBold();
                    row.AutoItem().PaddingLeft(6).Text(Reais(item.Subtotal));
                });
                if (!string.IsNullOrWhiteSpace(item.Observacao))
                    col.Item().PaddingLeft(8).Text($"obs: {item.Observacao}").FontSize(11).Italic().FontColor(MarcaDaCasa.CacauSuave);
            }

            Divisor(col);
            if (r.Frete > 0m) Valor(col, "Entrega (frete)", Reais(r.Frete));
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("Total").FontFamily(MarcaDaCasa.FonteTitulo).FontSize(18).Bold();
                row.AutoItem().Text(Reais(r.Total)).FontFamily(MarcaDaCasa.FonteTitulo).FontSize(18).Bold().FontColor(MarcaDaCasa.MarcaForte);
            });
            Linha(col, "Entrega", r.Entrega);
            Linha(col, "Endereço", r.Endereco);

            if (pix is not null && svgQr is not null) BlocoPix(col, pix, svgQr);
            if (!string.IsNullOrWhiteSpace(r.Fecho))
                col.Item().PaddingTop(6).Background(MarcaDaCasa.MarcaClaro).PaddingVertical(6).AlignCenter()
                    .Text(r.Fecho.Trim()).FontFamily(MarcaDaCasa.FonteTitulo).FontSize(15).Italic().FontColor(MarcaDaCasa.MarcaForte);
        });
    }

    private static void Selo(ColumnDescriptor col, DateTime? pagoEm) =>
        col.Item().AlignCenter().Background(MarcaDaCasa.Marca).PaddingVertical(4).PaddingHorizontal(16)
            .Text(pagoEm is { } quando ? $"Pago · {quando.ToString("dd/MM/yyyy 'às' HH:mm", PtBr)}" : "Pago")
            .FontSize(16).Bold().FontColor(MarcaDaCasa.Papel);

    private static void BlocoPix(ColumnDescriptor col, PixPdfDto pix, string svgQr)
    {
        Divisor(col);
        col.Item().AlignCenter().Text(pix.RestanteNaEntrega > 0m ? "Sinal pelo Pix" : "Pague pelo Pix")
            .FontFamily(MarcaDaCasa.FonteTitulo).FontSize(18).Bold().FontColor(MarcaDaCasa.Marca);
        col.Item().AlignCenter().Text(Reais(pix.ValorDoPix)).FontSize(20).Bold().FontColor(MarcaDaCasa.MarcaForte);
        if (pix.RestanteNaEntrega > 0m)
            col.Item().AlignCenter().Text($"Restante na entrega: {Reais(pix.RestanteNaEntrega)}").FontSize(12);
        if (!string.IsNullOrWhiteSpace(pix.Recebedor))
            col.Item().AlignCenter().Text($"Para {pix.Recebedor}").FontSize(12).FontColor(MarcaDaCasa.CacauSuave);
        col.Item().AlignCenter().Width(QrMm, Unit.Millimetre).Height(QrMm, Unit.Millimetre).Svg(svgQr);
        col.Item().AlignCenter().Text("Abra o app do seu banco e leia o QR, ou copie o código que vai na mensagem seguinte.")
            .FontSize(11).FontColor(MarcaDaCasa.CacauSuave);
        col.Item().Background(MarcaDaCasa.Creme).Padding(4).Text(pix.CopiaCola).FontSize(8).FontColor(MarcaDaCasa.Cacau);
    }

    /// <summary>SVG com um só caminho (sem frestas entre módulos) e a zona de silêncio de 4 módulos.</summary>
    internal static string SvgDoQr(bool[,] matriz)
    {
        var n = matriz.GetLength(0);
        var tamanho = n + 2 * ZonaDeSilencio;
        var sb = new StringBuilder(n * n);
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {tamanho} {tamanho}\" shape-rendering=\"crispEdges\">");
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{tamanho}\" height=\"{tamanho}\" fill=\"#FFFFFF\"/><path fill=\"#000000\" d=\"");
        for (var linha = 0; linha < n; linha++)
            for (var coluna = 0; coluna < n; coluna++)
                if (matriz[linha, coluna])
                    sb.Append(CultureInfo.InvariantCulture, $"M{coluna + ZonaDeSilencio} {linha + ZonaDeSilencio}h1v1h-1z");
        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    private static void Divisor(ColumnDescriptor col) =>
        col.Item().PaddingVertical(4).LineHorizontal(1f).LineColor(MarcaDaCasa.Linha);

    private static void Valor(ColumnDescriptor col, string rotulo, string valor) =>
        col.Item().Row(row =>
        {
            row.RelativeItem().Text(rotulo);
            row.AutoItem().Text(valor);
        });

    private static void Linha(ColumnDescriptor col, string rotulo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        col.Item().Text(t =>
        {
            t.Span($"{rotulo}: ").SemiBold();
            t.Span(valor);
        });
    }

    private static string Reais(decimal valor) => valor.ToString("C", PtBr);
}
