using FluentAssertions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EasyStock.Application.UseCases.Etiquetas.Folha;
using EasyStock.Infra.Async.Pdf;

namespace EasyStock.Infra.Async.UnitTests.Pdf;

public sealed class FolhaEtiquetasPdfRendererTests
{
    private const string ConteudoQr = "TALHARIM-LOT-261103-707082-001";
    private static readonly QrCoderEncoder Qr = new();
    private static readonly GerarFolhaEtiquetasPdfUseCase UseCase = new(new FolhaEtiquetasPdfRenderer(), Qr);

    private static JsonObject Texto(string id, string conteudo, decimal x, decimal y, decimal w, decimal h, decimal pt, int peso) => new()
    {
        ["id"] = id, ["type"] = "text", ["content"] = conteudo, ["font"] = "sans", ["size_pt"] = pt,
        ["weight"] = peso, ["align"] = "left", ["overflow"] = "clip",
        ["x_mm"] = x, ["y_mm"] = y, ["w_mm"] = w, ["h_mm"] = h,
    };

    private static JsonObject QrCodigo() => new()
    {
        ["id"] = "qr", ["type"] = "code", ["format"] = "qr", ["content"] = ConteudoQr, ["quiet_zone_mm"] = 0.5m,
        ["x_mm"] = 45m, ["y_mm"] = 3m, ["w_mm"] = 15.5m, ["h_mm"] = 15.5m,
    };

    private static JsonObject Barras() => new()
    {
        ["id"] = "barras", ["type"] = "code", ["format"] = "barcode-code128", ["content"] = "2611037070820001", ["quiet_zone_mm"] = 2.5m,
        ["x_mm"] = 4.125m, ["y_mm"] = 25.16m, ["w_mm"] = 35.75m, ["h_mm"] = 8m,
    };

    private static EtiquetaNaFolha Etiqueta(int? copias = null) => new(JsonSerializer.SerializeToElement(new JsonObject
    {
        ["v"] = 1,
        ["size"] = new JsonObject { ["preset"] = "custom", ["w_mm"] = 63.5m, ["h_mm"] = 38.1m, ["orientation"] = "horizontal" },
        ["elements"] = new JsonArray(
            Texto("nome", "Talharim Fresco de Espinafre", 1.5m, 5.03m, 41m, 4.06m, 10m, 700),
            Texto("val", "Val 06/11/2026", 1.5m, 9.5m, 41m, 4.13m, 9m, 700),
            QrCodigo(), Barras()),
    }), copias);

    private static GerarFolhaEtiquetasPdfCommand Pedido(int pular, params EtiquetaNaFolha[] etiquetas) =>
        new(new FolhaSpec("pimaco-a4256", null, null, null), Guias: true, pular, etiquetas);

    private static int Paginas(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b(?!s)").Count;

    [Fact]
    public async Task GeraPdfValidoComUmaPaginaParaUmaEtiqueta()
    {
        var pdf = await UseCase.ExecuteAsync(Pedido(0, Etiqueta()));

        var texto = Encoding.Latin1.GetString(pdf);
        texto.Should().StartWith("%PDF-");
        texto.TrimEnd().Should().EndWith("%%EOF");
        texto.Should().NotContain("/JavaScript");
        Paginas(pdf).Should().Be(1);
    }

    [Theory]
    [InlineData(21, 0, 1)]
    [InlineData(22, 0, 2)]
    [InlineData(5, 20, 2)]
    [InlineData(42, 0, 2)]
    [InlineData(43, 0, 3)]
    public async Task NumeroDePaginasAcompanhaEtiquetasEPosicoesPuladas(int copias, int pular, int paginasEsperadas)
    {
        var pdf = await UseCase.ExecuteAsync(Pedido(pular, Etiqueta(copias)));

        Paginas(pdf).Should().Be(paginasEsperadas);
    }

    [Fact]
    public async Task PdfIncluiOsDesenhosVetoriaisDeQrEBarras()
    {
        var plano = UseCase.Planejar(Pedido(0, Etiqueta()));
        var elementos = plano.Paginas[0].Celulas[0].Etiqueta!.Elementos;

        elementos.OfType<QrPlano>().Should().ContainSingle();
        elementos.OfType<BarrasPlano>().Should().ContainSingle();
        var pdf = await new FolhaEtiquetasPdfRenderer().RenderAsync(plano);
        pdf.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public async Task RendererRespeitaCancelamento()
    {
        var plano = UseCase.Planejar(Pedido(0, Etiqueta()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var acao = () => new FolhaEtiquetasPdfRenderer().RenderAsync(plano, cts.Token);

        await acao.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void QrEncoderDevolveMatrizQuadradaEmModoByteECcM()
    {
        var matriz = Qr.Matriz(ConteudoQr);

        matriz.GetLength(0).Should().Be(matriz.GetLength(1));
        // Versão 3 (29 módulos) é o tamanho do modo byte, ECC M, para 29 caracteres.
        matriz.GetLength(0).Should().Be(29);
        // Finder pattern: canto superior esquerdo escuro, espaço separador claro ao redor.
        matriz[0, 0].Should().BeTrue();
        matriz[0, 6].Should().BeTrue();
        matriz[7, 7].Should().BeFalse();
    }

    [Fact]
    public void QrEncoderRecusaConteudoVazio()
    {
        var acao = () => Qr.Matriz("");

        acao.Should().Throw<ArgumentException>();
    }
}
