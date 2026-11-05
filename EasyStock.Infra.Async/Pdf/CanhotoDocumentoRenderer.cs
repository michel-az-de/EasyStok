using System.Globalization;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Application.UseCases.Operacao.Impressao;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// O canhoto da comanda em 80 mm de largura e altura contínua (#2211), o mesmo conteúdo do papel da impressora (S20).
/// PNG em 200 dpi para o WhatsApp mostrar legível no celular; PDF para quem quiser o documento.
/// </summary>
public sealed class CanhotoDocumentoRenderer : IRenderizadorCanhoto
{
    private const float LarguraMm = 80;
    private const int DpiImagem = 200;
    private static readonly CultureInfo PtBr = new("pt-BR");

    static CanhotoDocumentoRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public Task<AnexoGerado> RenderizarAsync(CanhotoDto canhoto, FormatoAnexo formato, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(canhoto);
        ct.ThrowIfCancellationRequested();

        var documento = Document.Create(d => d.Page(page => Desenhar(page, canhoto)));
        var anexo = formato == FormatoAnexo.Pdf
            ? new AnexoGerado(documento.GeneratePdf(), "application/pdf", "pdf")
            : new AnexoGerado(
                documento.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = DpiImagem })
                    .First(),
                "image/png", "png");
        return Task.FromResult(anexo);
    }

    private static void Desenhar(PageDescriptor page, CanhotoDto canhoto)
    {
        var c = canhoto.Cabecalho;
        page.ContinuousSize(LarguraMm, Unit.Millimetre);
        page.Margin(4, Unit.Millimetre);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Black));

        page.Content().Column(col =>
        {
            col.Spacing(3);
            col.Item().AlignCenter().Text(c.NomeCasa).FontSize(13).Bold();
            col.Item().AlignCenter().Text($"Pedido #{c.Numero}").FontSize(16).Bold();
            Linha(col, "Cliente", c.Cliente);
            Linha(col, "Telefone", c.Telefone);
            Linha(col, "Entrega", c.Endereco);
            Linha(col, "Para", c.AgendadoPara is { } quando ? Data(quando) : "agora");
            Linha(col, "Pago em", c.PagoEm is { } pago ? Data(pago) : null);

            foreach (var grupo in canhoto.Grupos)
            {
                col.Item().PaddingTop(4).LineHorizontal(0.8f);
                col.Item().Text(grupo.Titulo.ToUpper(PtBr)).FontSize(11).Bold();
                foreach (var item in grupo.Itens)
                {
                    var porcao = string.IsNullOrWhiteSpace(item.Porcao) ? "" : $" ({item.Porcao})";
                    col.Item().Text($"{item.Quantidade.ToString("0.###", PtBr)} × {item.Nome}{porcao}").SemiBold();
                    if (!string.IsNullOrWhiteSpace(item.Molho))
                        col.Item().PaddingLeft(8).Text($"molho: {item.Molho}").FontSize(9);
                    if (!string.IsNullOrWhiteSpace(item.Observacao))
                        col.Item().PaddingLeft(8).Text($"obs: {item.Observacao}").FontSize(9).Italic();
                }
            }

            if (!string.IsNullOrWhiteSpace(canhoto.Observacoes))
            {
                col.Item().PaddingTop(4).LineHorizontal(0.8f);
                col.Item().Text($"Observações: {canhoto.Observacoes}");
            }

            col.Item().PaddingTop(6).AlignCenter().Text(canhoto.Rodape).FontSize(8);
        });
    }

    private static void Linha(ColumnDescriptor col, string rotulo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        col.Item().Text(t =>
        {
            t.Span($"{rotulo}: ").SemiBold();
            t.Span(valor);
        });
    }

    private static string Data(DateTime instante) => instante.ToString("dd/MM HH:mm", PtBr);
}
