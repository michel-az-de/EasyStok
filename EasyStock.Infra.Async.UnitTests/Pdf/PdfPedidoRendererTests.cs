using FluentAssertions;
using System.Text;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Infra.Async.Pdf;

namespace EasyStock.Infra.Async.UnitTests.Pdf;

/// <summary>#2962 (E3): o PDF do pedido leva o QR do copia e cola; o comprovante "Pago" não leva o código.</summary>
public sealed class PdfPedidoRendererTests
{
    private const string CopiaCola = "00020126580014BR.GOV.BCB.PIX0136chave-secreta-da-loja5204000053039865802BR5909CASA BABA6009SAO PAULO62070503***6304ABCD";

    private static ResumoPedidoDto Resumo(int itens = 2, bool pago = false) => new(
        "Casa da Baba", "A1B2C3",
        Enumerable.Range(1, itens).Select(i => new ResumoPedidoItemDto($"Lasanha {i}", i, 85m * i, i == 1 ? "sem sal" : null)).ToList(),
        0m, 170m, "20/11 às 19:30", "Rua das Flores, 10", pago);

    private static PixPdfDto Pix(decimal restante = 0m) => new(CopiaCola, 170m - restante, restante, "CASA DA BABA");

    private sealed class QrEspiao : IQrCodeEncoder
    {
        public string? Conteudo { get; private set; }

        public bool[,] Matriz(string conteudo)
        {
            Conteudo = conteudo;
            return new QrCoderEncoder().Matriz(conteudo);
        }
    }

    private static bool ComecaComPdf(byte[] bytes) => Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    [Fact]
    public async Task PedidoSaiComoPdfValidoEOQrLevaOCopiaECola()
    {
        var qr = new QrEspiao();

        var anexo = await new PdfPedidoRenderer(qr).RenderizarPedidoAsync(Resumo(), Pix());

        anexo.ContentType.Should().Be("application/pdf");
        anexo.Extensao.Should().Be("pdf");
        ComecaComPdf(anexo.Conteudo).Should().BeTrue();
        anexo.Conteudo.Length.Should().BeGreaterThan(2000);
        qr.Conteudo.Should().Be(CopiaCola);
    }

    [Fact]
    public async Task PedidoLongoCrescePeloMesmoDocumento()
    {
        var renderer = new PdfPedidoRenderer(new QrCoderEncoder());

        var curto = await renderer.RenderizarPedidoAsync(Resumo(2), Pix());
        var longo = await renderer.RenderizarPedidoAsync(Resumo(40), Pix());

        ComecaComPdf(longo.Conteudo).Should().BeTrue();
        longo.Conteudo.Length.Should().BeGreaterThan(curto.Conteudo.Length);
    }

    [Fact]
    public async Task SinalDaEncomendaGeraPdfValido()
    {
        var anexo = await new PdfPedidoRenderer(new QrCoderEncoder()).RenderizarPedidoAsync(Resumo(), Pix(restante: 85m));

        ComecaComPdf(anexo.Conteudo).Should().BeTrue();
    }

    [Fact]
    public async Task ComprovanteSaiComoPdfSemOQrNemOCopiaECola()
    {
        var qr = new QrEspiao();

        var anexo = await new PdfPedidoRenderer(qr).RenderizarComprovanteAsync(Resumo(pago: true), new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc));

        ComecaComPdf(anexo.Conteudo).Should().BeTrue();
        qr.Conteudo.Should().BeNull("o comprovante não leva o código Pix");
        Encoding.Latin1.GetString(anexo.Conteudo).Should().NotContain("chave-secreta");
    }

    [Fact]
    public void SvgDoQrTemUmCaminhoSoEZonaDeSilencio()
    {
        var matriz = new QrCoderEncoder().Matriz(CopiaCola);

        var svg = PdfPedidoRenderer.SvgDoQr(matriz);

        var lado = matriz.GetLength(0) + 8;
        svg.Should().Contain($"viewBox=\"0 0 {lado} {lado}\"");
        svg.Split("<path").Length.Should().Be(2);
    }
}
