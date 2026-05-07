using EasyStock.Application.Ports.Output.Storage;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace EasyStock.Api.Services;

/// <summary>
/// Otimiza imagens usando SkiaSharp: redimensiona e converte para WebP.
/// Fallback seguro: se falhar, retorna a imagem original.
/// </summary>
public sealed class SkiaImageProcessor(ILogger<SkiaImageProcessor> logger) : IImageProcessor
{
    public (byte[] Data, string ContentType, string Extension) Optimize(
        byte[] source,
        string originalContentType,
        int maxSide = 1920,
        int quality = 85)
    {
        try
        {
            using var original = SKBitmap.Decode(source);
            if (original is null)
            {
                logger.LogWarning("SkiaSharp nao conseguiu decodificar a imagem. Retornando original.");
                return FallbackOriginal(source, originalContentType);
            }

            var (newWidth, newHeight) = CalcularDimensoes(original.Width, original.Height, maxSide);

            SKBitmap target;
            if (newWidth != original.Width || newHeight != original.Height)
            {
                // Redimensionar
                target = original.Resize(new SKImageInfo(newWidth, newHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (target is null)
                {
                    logger.LogWarning("SkiaSharp falhou ao redimensionar {W}x{H} -> {NW}x{NH}. Usando original.",
                        original.Width, original.Height, newWidth, newHeight);
                    target = original;
                }
            }
            else
            {
                target = original;
            }

            // Encodar como WebP
            using var image = SKImage.FromBitmap(target);
            using var data = image.Encode(SKEncodedImageFormat.Webp, quality);

            if (target != original)
                target.Dispose();

            if (data is null || data.Size == 0)
            {
                logger.LogWarning("SkiaSharp falhou ao encodar WebP. Retornando original.");
                return FallbackOriginal(source, originalContentType);
            }

            var result = data.ToArray();

            logger.LogInformation(
                "Imagem otimizada: {OrigW}x{OrigH} ({OrigSize:F1}KB) -> {NewW}x{NewH} WebP ({NewSize:F1}KB, -{Reduction:F0}%)",
                original.Width, original.Height, source.Length / 1024.0,
                newWidth, newHeight, result.Length / 1024.0,
                (1.0 - (double)result.Length / source.Length) * 100);

            return (result, "image/webp", ".webp");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao otimizar imagem ({Size} bytes). Retornando original.", source.Length);
            return FallbackOriginal(source, originalContentType);
        }
    }

    private static (int width, int height) CalcularDimensoes(int origWidth, int origHeight, int maxSide)
    {
        if (origWidth <= maxSide && origHeight <= maxSide)
            return (origWidth, origHeight);

        double ratio = (double)origWidth / origHeight;

        if (origWidth >= origHeight)
            return (maxSide, (int)Math.Round(maxSide / ratio));
        else
            return ((int)Math.Round(maxSide * ratio), maxSide);
    }

    private static (byte[] Data, string ContentType, string Extension) FallbackOriginal(
        byte[] source, string contentType)
    {
        var ext = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        return (source, contentType, ext);
    }
}
