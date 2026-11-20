using System.Globalization;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Notifications.Anexos;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// O resumo do pedido para o cliente conferir no WhatsApp (#2544, S73, D2): 90 mm de largura e altura contínua, PNG em
/// 200 dpi, numa imagem só. Desde a #2929 sai com a marca: Lora nos títulos, Nunito Sans no corpo e as cores da marca
/// (<see cref="MarcaDaCasa"/>). As fontes vão embutidas no assembly, porque o servidor não as tem instaladas.
/// </summary>
public sealed class ResumoPedidoRenderer : IRenderizadorResumoPedido
{
    private const float LarguraMm = 90;
    private const int DpiImagem = 200;
    private const string FonteTitulo = MarcaDaCasa.FonteTitulo;
    private const string FonteCorpo = MarcaDaCasa.FonteCorpo;
    private const string Marca = MarcaDaCasa.Marca;
    private const string MarcaForte = MarcaDaCasa.MarcaForte;
    private const string MarcaClaro = MarcaDaCasa.MarcaClaro;
    private const string Tinta = MarcaDaCasa.Cacau;
    private const string Tinta3 = MarcaDaCasa.CacauSuave;
    private const string Papel = MarcaDaCasa.Papel;

    private static readonly CultureInfo PtBr = new("pt-BR");

    static ResumoPedidoRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        RegistrarFonte("Lora.ttf");
        // A Nunito Sans vai em instâncias estáticas sob um nome só: o QuestPDF escolhe pelo peso de cada arquivo.
        foreach (var peso in new[] { "Regular", "SemiBold", "Bold" })
            RegistrarFonte($"NunitoSans-{peso}.ttf", FonteCorpo);
    }

    private static void RegistrarFonte(string arquivo, string? familia = null)
    {
        var nome = $"EasyStock.Infra.Async.Pdf.Fontes.{arquivo}";
        using var fonte = typeof(ResumoPedidoRenderer).Assembly.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"Fonte da marca ausente no assembly: {nome}.");
        if (familia is null) FontManager.RegisterFont(fonte);
        else FontManager.RegisterFontWithCustomName(familia, fonte);
    }

    public Task<AnexoGerado> RenderizarAsync(ResumoPedidoDto resumo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        ct.ThrowIfCancellationRequested();

        var imagem = Document.Create(d => d.Page(page => Desenhar(page, resumo)))
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = DpiImagem })
            .First();
        return Task.FromResult(new AnexoGerado(imagem, "image/png", "png"));
    }

    private static void Desenhar(PageDescriptor page, ResumoPedidoDto r)
    {
        page.ContinuousSize(LarguraMm, Unit.Millimetre);
        page.Margin(6, Unit.Millimetre);
        page.PageColor(Papel);
        page.DefaultTextStyle(t => t.FontFamily(FonteCorpo).FontSize(11).FontColor(Tinta));

        page.Content().Column(col =>
        {
            col.Spacing(3);
            if (!string.IsNullOrWhiteSpace(r.NomeLoja))
                col.Item().AlignCenter().Text(r.NomeLoja).FontFamily(FonteTitulo).FontSize(17).Bold().FontColor(Marca);
            col.Item().AlignCenter().Text($"Resumo do pedido {r.Codigo}").FontSize(10).FontColor(Tinta3);
            Divisor(col);

            foreach (var item in r.Itens)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text($"{item.Quantidade.ToString("0.###", PtBr)} × {item.Nome}").SemiBold();
                    row.AutoItem().PaddingLeft(6).Text(Reais(item.Subtotal));
                });
                if (!string.IsNullOrWhiteSpace(item.Observacao))
                    col.Item().PaddingLeft(8).Text($"obs: {item.Observacao}").FontSize(9).Italic().FontColor(Tinta3);
            }

            Divisor(col);
            Valor(col, "Entrega", Reais(r.Frete));
            col.Item().Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text("Total").FontFamily(FonteTitulo).FontSize(15).Bold();
                if (r.Pago)
                    row.AutoItem().AlignMiddle().PaddingRight(6).Background(Marca).PaddingVertical(1).PaddingHorizontal(6)
                        .Text("Pago").FontSize(9).Bold().FontColor(Papel);
                row.AutoItem().AlignMiddle().Text(Reais(r.Total)).FontFamily(FonteTitulo).FontSize(15).Bold().FontColor(MarcaForte);
            });
            Divisor(col);
            Linha(col, "Data", r.Entrega);
            Linha(col, "Endereço", r.Endereco);

            if (!string.IsNullOrWhiteSpace(r.Fecho))
                col.Item().PaddingTop(8).Background(MarcaClaro).PaddingVertical(5).AlignCenter()
                    .Text(r.Fecho.Trim()).FontFamily(FonteTitulo).FontSize(14).Italic().FontColor(MarcaForte);
        });
    }

    private static void Divisor(ColumnDescriptor col) =>
        col.Item().PaddingVertical(4).LineHorizontal(0.8f).LineColor(MarcaClaro);

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
