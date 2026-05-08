using EasyStock.Domain.Entities.Fiscal;

namespace EasyStock.Application.Ports.Output.Fiscal;

public interface ICertificadoA1Repository
{
    Task<NotaFiscalCertificadoA1?> ObterAtivoAsync(Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<NotaFiscalCertificadoA1>> ListarPorEmpresaAsync(Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<NotaFiscalCertificadoA1>> ListarExpirandoAsync(int diasAhead, CancellationToken ct);
    Task AdicionarAsync(NotaFiscalCertificadoA1 cert, CancellationToken ct);
    Task AtualizarAsync(NotaFiscalCertificadoA1 cert, CancellationToken ct);
}
