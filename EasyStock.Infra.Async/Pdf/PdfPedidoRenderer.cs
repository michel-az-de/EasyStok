using System.Globalization;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Notifications.Anexos;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// #2962 (E3): o comprovante "Pago" em PDF, feito para a tela do celular: página de 110 mm de largura e altura contínua
/// (cabe em 390 px sem zoom), fonte grande e alto contraste, na marca (<see cref="MarcaDaCasa"/>). O pedido com o QR Pix
/// sai como imagem do impresso <c>pedido.sbn</c> (#3035); o desenho dele aqui saiu na #3049.
/// </summary>
public sealed class PdfPedidoRenderer : IRenderizadorPdfPedido
{
    private const float LarguraMm = 110;

    private static readonly CultureInfo PtBr = new("pt-BR");

    static PdfPedidoRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        FontesDaMarca.Registrar();
    }

    public Task<AnexoGerado> RenderizarComprovanteAsync(ResumoPedidoDto resumo, DateTime? pagoEm, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Pdf(page => Desenhar(page, resumo, pagoEm)));
    }

    private static AnexoGerado Pdf(Action<QuestPDF.Fluent.PageDescriptor> pagina) =>
        new(Document.Create(d => d.Page(pagina)).GeneratePdf(), "application/pdf", "pdf");

    private static void Desenhar(QuestPDF.Fluent.PageDescriptor page, ResumoPedidoDto r, DateTime? pagoEm)
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
            col.Item().AlignCenter().Text($"Comprovante do pedido {r.Codigo}")
                .FontSize(15).SemiBold().FontColor(MarcaDaCasa.CacauSuave);
            Selo(col, pagoEm);
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

            if (!string.IsNullOrWhiteSpace(r.Fecho))
                col.Item().PaddingTop(6).Background(MarcaDaCasa.MarcaClaro).PaddingVertical(6).AlignCenter()
                    .Text(r.Fecho.Trim()).FontFamily(MarcaDaCasa.FonteTitulo).FontSize(15).Italic().FontColor(MarcaDaCasa.MarcaForte);
        });
    }

    private static void Selo(ColumnDescriptor col, DateTime? pagoEm) =>
        col.Item().AlignCenter().Background(MarcaDaCasa.Marca).PaddingVertical(4).PaddingHorizontal(16)
            .Text(pagoEm is { } quando ? $"Pago · {quando.ToString("dd/MM/yyyy 'às' HH:mm", PtBr)}" : "Pago")
            .FontSize(16).Bold().FontColor(MarcaDaCasa.Papel);

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
