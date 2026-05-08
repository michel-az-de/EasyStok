using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class NotaFiscalCertificadoA1Repository(EasyStockDbContext db) : ICertificadoA1Repository
{
    public Task<NotaFiscalCertificadoA1?> ObterAtivoAsync(Guid empresaId, CancellationToken ct) =>
        db.NotasFiscaisCertificadosA1
            .Where(c => c.EmpresaId == empresaId && c.Ativo)
            .OrderByDescending(c => c.CriadoEm)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<NotaFiscalCertificadoA1>> ListarPorEmpresaAsync(Guid empresaId, CancellationToken ct) =>
        await db.NotasFiscaisCertificadosA1
            .Where(c => c.EmpresaId == empresaId)
            .OrderByDescending(c => c.CriadoEm)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<NotaFiscalCertificadoA1>> ListarExpirandoAsync(int diasAhead, CancellationToken ct)
    {
        var corte = DateTime.UtcNow.AddDays(diasAhead);
        return await db.NotasFiscaisCertificadosA1
            .IgnoreQueryFilters()
            .Where(c => c.Ativo && c.ValidoAte <= corte)
            .OrderBy(c => c.ValidoAte)
            .ToListAsync(ct);
    }

    public Task AdicionarAsync(NotaFiscalCertificadoA1 cert, CancellationToken ct)
        => db.NotasFiscaisCertificadosA1.AddAsync(cert, ct).AsTask();

    public Task AtualizarAsync(NotaFiscalCertificadoA1 cert, CancellationToken ct)
    {
        db.NotasFiscaisCertificadosA1.Update(cert);
        return Task.CompletedTask;
    }
}
