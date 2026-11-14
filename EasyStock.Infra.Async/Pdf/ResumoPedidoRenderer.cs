using System.Globalization;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Notifications.Anexos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// O resumo do pedido para o cliente conferir no WhatsApp (#2544, S73, D2): 90 mm de largura e altura contínua, PNG em
/// 200 dpi, numa imagem só. Mesmo desenho do canhoto (<see cref="CanhotoDocumentoRenderer"/>), com valores.
/// </summary>
public sealed class ResumoPedidoRenderer : IRenderizadorResumoPedido
{
    private const float LarguraMm = 90;
    private const int DpiImagem = 200;
    private static readonly CultureInfo PtBr = new("pt-BR");

    static ResumoPedidoRenderer() => QuestPDF.Settings.License = LicenseType.Community;

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
        page.Margin(5, Unit.Millimetre);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontSize(11).FontColor(Colors.Black));

        page.Content().Column(col =>
        {
            col.Spacing(3);
            if (!string.IsNullOrWhiteSpace(r.NomeLoja))
                col.Item().AlignCenter().Text(r.NomeLoja).FontSize(13).Bold();
            col.Item().AlignCenter().Text($"Resumo do pedido {r.Codigo}").FontSize(15).Bold();
            col.Item().PaddingTop(4).LineHorizontal(0.8f);

            foreach (var item in r.Itens)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text($"{item.Quantidade.ToString("0.###", PtBr)} × {item.Nome}").SemiBold();
                    row.AutoItem().Text(Reais(item.Subtotal));
                });
                if (!string.IsNullOrWhiteSpace(item.Observacao))
                    col.Item().PaddingLeft(8).Text($"obs: {item.Observacao}").FontSize(9).Italic();
            }

            col.Item().PaddingTop(4).LineHorizontal(0.8f);
            Valor(col, "Entrega", Reais(r.Frete));
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("Total").FontSize(13).Bold();
                row.AutoItem().Text(Reais(r.Total)).FontSize(13).Bold();
            });
            col.Item().PaddingTop(4).LineHorizontal(0.8f);
            Linha(col, "Data", r.Entrega);
            Linha(col, "Endereço", r.Endereco);
        });
    }

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
