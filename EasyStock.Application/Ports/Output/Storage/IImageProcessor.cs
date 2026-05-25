namespace EasyStock.Application.Ports.Output.Storage;

/// <summary>
/// Otimiza imagens para web: redimensiona e comprime mantendo qualidade visual.
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Otimiza uma imagem: redimensiona para maxSide e comprime como WebP.
    /// Retorna (bytes otimizados, content-type, extensao com ponto).
    /// Se o processamento falhar, retorna o original sem alterar.
    /// </summary>
    (byte[] Data, string ContentType, string Extension) Optimize(
        byte[] source,
        string originalContentType,
        int maxSide = 1920,
        int quality = 85);
}
