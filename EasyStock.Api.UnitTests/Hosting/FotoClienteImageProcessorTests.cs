using EasyStock.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace EasyStock.Api.UnitTests.Hosting;

public sealed class FotoClienteImageProcessorTests
{
    [Theory]
    [InlineData(1024, 768, 512, 384)]
    [InlineData(1024, 1, 512, 1)]
    [InlineData(1, 1024, 1, 512)]
    [InlineData(100, 75, 100, 75)]
    public void FotoValidaViraJpegComNoMaximo512Pixels(int largura, int altura, int esperadaLargura, int esperadaAltura)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(SKColors.DarkOrange);
        using var imagem = SKImage.FromBitmap(bitmap);
        using var png = imagem.Encode(SKEncodedImageFormat.Png, 100);
        var fonte = png.ToArray();
        var (foto, mime, extensao) = new SkiaImageProcessor(NullLogger<SkiaImageProcessor>.Instance)
            .Optimize(fonte, "image/png", 512, 85, jpeg: true);
        foto.Should().NotBeSameAs(fonte);
        mime.Should().Be("image/jpeg");
        extensao.Should().Be(".jpg");
        using var decodificada = SKBitmap.Decode(foto);
        decodificada.Width.Should().Be(esperadaLargura);
        decodificada.Height.Should().Be(esperadaAltura);
    }

    [Fact]
    public void FotoTruncadaConservaOriginalParaUseCaseRecusarFallback()
    {
        byte[] fonte = [0xFF, 0xD8, 0xFF, 0xE0, 1];
        var (foto, _, _) = new SkiaImageProcessor(NullLogger<SkiaImageProcessor>.Instance)
            .Optimize(fonte, "image/jpeg", 512, 85, jpeg: true);
        foto.Should().BeSameAs(fonte);
    }
}
