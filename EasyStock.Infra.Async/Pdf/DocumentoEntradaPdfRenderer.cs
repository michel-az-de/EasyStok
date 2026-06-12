using System.Globalization;
using EasyStock.Application.Ports.Output.Pdf;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// Renderiza os PDFs de uma entrada de estoque (etiqueta do lote + Nota de Entrada),
/// ambos com QRCode. QuestPDF (Community MIT) + QRCoder (<see cref="PngByteQRCode"/>,
/// encoder gerenciado puro, sem System.Drawing — roda no container Linux).
/// Stateless e threadsafe: pode ser Singleton no DI.
/// </summary>
public sealed class DocumentoEntradaPdfRenderer : IDocumentoEntradaPdfRenderer
{
    static DocumentoEntradaPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> RenderEtiquetaAsync(DocumentoEntradaPdfData data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ct.ThrowIfCancellationRequested();
        var qr = GerarQrPng(data.QrConteudo);
        return Task.FromResult(new EtiquetaEntradaTemplate(data, qr).GeneratePdf());
    }

    public Task<byte[]> RenderNotaAsync(DocumentoEntradaPdfData data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ct.ThrowIfCancellationRequested();
        var qr = GerarQrPng(data.QrConteudo);
        return Task.FromResult(new NotaEntradaTemplate(data, qr).GeneratePdf());
    }

    /// <summary>PNG do QRCode via encoder gerenciado puro (Linux/container ok).</summary>
    internal static byte[] GerarQrPng(string conteudo)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(string.IsNullOrWhiteSpace(conteudo) ? " " : conteudo, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(qrData).GetGraphic(20);
    }
}

/// <summary>Etiqueta compacta do lote (rótulo ~80x50mm) com QRCode do código.</summary>
internal sealed class EtiquetaEntradaTemplate(DocumentoEntradaPdfData data, byte[] qrPng) : IDocument
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(new PageSize(80, 50, Unit.Millimetre));
            page.Margin(4, Unit.Millimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Grey.Darken4));

            page.Content().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Spacing(1);
                    col.Item().Text(data.EmpresaNome).FontSize(7).FontColor(Colors.Grey.Darken1);
                    col.Item().Text(data.ProdutoNome).Bold().FontSize(10);
                    if (!string.IsNullOrWhiteSpace(data.ProdutoSku))
                        col.Item().Text($"SKU {data.ProdutoSku}").FontSize(7).FontColor(Colors.Grey.Medium);

                    col.Item().PaddingTop(2).Text(t =>
                    {
                        t.Span("LOTE ").FontSize(7).FontColor(Colors.Grey.Medium);
                        t.Span(data.LoteCodigo ?? "—").Bold().FontFamily(Fonts.CourierNew);
                    });
                    if (data.Validade is { } val)
                        col.Item().Text($"VAL {val:dd/MM/yyyy}").FontSize(8);
                    col.Item().Text($"QTD {data.Quantidade.ToString("0.###", PtBr)}").FontSize(8);
                });

                row.ConstantItem(26, Unit.Millimetre).AlignRight().AlignMiddle().Image(qrPng);
            });
        });
    }
}

/// <summary>Nota de Entrada A4 (emissor, item, totais, lote/validade) com QRCode.</summary>
internal sealed class NotaEntradaTemplate(DocumentoEntradaPdfData data, byte[] qrPng) : IDocument
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(ComposeEmissor);
                col.Item().Element(ComposeMeta);
                col.Item().Element(ComposeItem);
                col.Item().AlignRight().Element(ComposeTotais);
                col.Item().Element(ComposeLoteBox);
                if (!string.IsNullOrWhiteSpace(data.Observacoes))
                    col.Item().Element(ComposeObservacoes);
            });
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("NOTA DE ENTRADA").FontSize(22).Bold().FontColor(Colors.Indigo.Darken3);
                col.Item().Text($"Nº {data.NumeroDocumento}").FontSize(14).SemiBold();
                col.Item().PaddingTop(2).Text($"Entrada em {data.DataEntrada:dd/MM/yyyy}")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });
            row.ConstantItem(90).AlignRight().Image(qrPng);
        });
    }

    private void ComposeEmissor(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Spacing(2);
            col.Item().Text("EMISSOR").FontSize(8).FontColor(Colors.Grey.Medium).Bold();
            col.Item().Text(data.EmpresaNome).Bold();
            if (!string.IsNullOrWhiteSpace(data.EmpresaDocumento))
                col.Item().Text($"CNPJ/CPF: {data.EmpresaDocumento}");
        });
    }

    private void ComposeMeta(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("FORNECEDOR").FontSize(8).FontColor(Colors.Grey.Medium).Bold();
                col.Item().Text(string.IsNullOrWhiteSpace(data.FornecedorNome) ? "—" : data.FornecedorNome);
            });
            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text("DATA DA ENTRADA").FontSize(8).FontColor(Colors.Grey.Medium).Bold();
                col.Item().Text(data.DataEntrada.ToString("dd/MM/yyyy", PtBr));
            });
        });
    }

    private void ComposeItem(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(5); // produto
                c.RelativeColumn(2); // qtd
                c.RelativeColumn(2); // unit
                c.RelativeColumn(2); // subtotal
            });

            table.Header(h =>
            {
                static IContainer S(IContainer c) =>
                    c.DefaultTextStyle(t => t.FontSize(9).Bold().FontColor(Colors.Grey.Medium))
                     .PaddingVertical(6).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);
                h.Cell().Element(S).Text("PRODUTO");
                h.Cell().Element(S).AlignRight().Text("QTD");
                h.Cell().Element(S).AlignRight().Text("CUSTO UNIT");
                h.Cell().Element(S).AlignRight().Text("SUBTOTAL");
            });

            static IContainer C(IContainer c) =>
                c.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);

            table.Cell().Element(C).Text(t =>
            {
                t.Span(data.ProdutoNome);
                if (!string.IsNullOrWhiteSpace(data.ProdutoSku))
                    t.Span($"  ({data.ProdutoSku})").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
            var unidade = string.IsNullOrWhiteSpace(data.UnidadeLabel) ? "" : $" {data.UnidadeLabel}";
            table.Cell().Element(C).AlignRight().Text($"{data.Quantidade.ToString("0.###", PtBr)}{unidade}");
            table.Cell().Element(C).AlignRight().Text(data.CustoUnitario.ToString("C", PtBr));
            table.Cell().Element(C).AlignRight().Text(data.Total.ToString("C", PtBr));
        });
    }

    private void ComposeTotais(IContainer container)
    {
        container.Width(240).Column(col =>
        {
            col.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Text("TOTAL DA ENTRADA").FontSize(12).Bold();
                r.RelativeItem().AlignRight().Text(data.Total.ToString("C", PtBr)).FontSize(14).Bold();
            });
        });
    }

    private void ComposeLoteBox(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("LOTE").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
                col.Item().Text(data.LoteCodigo ?? "—").FontFamily(Fonts.CourierNew).Bold();
            });
            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text("VALIDADE").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
                col.Item().Text(data.Validade is { } v ? v.ToString("dd/MM/yyyy", PtBr) : "—");
            });
        });
    }

    private void ComposeObservacoes(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Item().Text("OBSERVAÇÕES").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
            col.Item().Text(data.Observacoes ?? string.Empty).FontSize(10);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span("Documento gerado em ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.Span(DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'", PtBr)).FontSize(8);
                t.Span("  ·  Doc ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.Span(data.NumeroDocumento).FontSize(8).FontFamily(Fonts.CourierNew);
            });
            row.AutoItem().AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                t.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }
}
