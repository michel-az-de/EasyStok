using EasyStock.Application.UseCases.Etiquetas.Folha;

namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>Desenha a folha A4 de etiquetas já planejada em PDF vetorial (#2200).</summary>
public interface IFolhaEtiquetasPdfRenderer
{
    Task<byte[]> RenderAsync(PlanoFolhaEtiquetas plano, CancellationToken ct = default);
}
