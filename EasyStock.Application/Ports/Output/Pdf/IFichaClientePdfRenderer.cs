using EasyStock.Application.UseCases.Cliente.Dossie;

namespace EasyStock.Application.Ports.Output.Pdf;

public interface IFichaClientePdfRenderer
{
    Task<byte[]> RenderAsync(DossieClienteDto dossie, byte[]? foto, DateTime consultaEm, CancellationToken ct = default);
}
