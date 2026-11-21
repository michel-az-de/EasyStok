using FluentAssertions;
using System.Text;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Infra.Async.Pdf;

namespace EasyStock.Infra.Async.UnitTests.Pdf;

/// <summary>#2962 (E3): o comprovante "Pago" sai em PDF. O pedido com o QR Pix é imagem do impresso (#3035).</summary>
public sealed class PdfPedidoRendererTests
{
    private static ResumoPedidoDto Resumo(int itens = 2) => new(
        "Casa da Baba", "A1B2C3",
        Enumerable.Range(1, itens).Select(i => new ResumoPedidoItemDto($"Lasanha {i}", i, 85m * i, i == 1 ? "sem sal" : null)).ToList(),
        0m, 170m, "20/11 às 19:30", "Rua das Flores, 10", true);

    private static readonly DateTime PagoEm = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    private static bool ComecaComPdf(byte[] bytes) => Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    [Fact]
    public async Task ComprovanteSaiComoPdfValido()
    {
        var anexo = await new PdfPedidoRenderer().RenderizarComprovanteAsync(Resumo(), PagoEm);

        anexo.ContentType.Should().Be("application/pdf");
        anexo.Extensao.Should().Be("pdf");
        ComecaComPdf(anexo.Conteudo).Should().BeTrue();
    }

    [Fact]
    public async Task ComprovanteLongoCrescePeloMesmoDocumento()
    {
        var renderer = new PdfPedidoRenderer();

        var curto = await renderer.RenderizarComprovanteAsync(Resumo(2), PagoEm);
        var longo = await renderer.RenderizarComprovanteAsync(Resumo(40), PagoEm);

        ComecaComPdf(longo.Conteudo).Should().BeTrue();
        longo.Conteudo.Length.Should().BeGreaterThan(curto.Conteudo.Length);
    }
}
