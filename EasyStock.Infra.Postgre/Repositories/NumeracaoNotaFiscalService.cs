using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.Exceptions.Fiscal;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Reserva próximo número fiscal usando SELECT FOR UPDATE em
/// nota_fiscal_contador (ADR-004). Lock pessimista garante FAIRNESS
/// e ausência de buracos artificiais por rollback.
/// </summary>
public sealed class NumeracaoNotaFiscalService(EasyStockDbContext db, IUnitOfWork uow)
    : INumeracaoNotaFiscalService
{
    public Task<int> ReservarProximoNumeroAsync(
        Guid empresaId, Guid lojaId, ModeloDocumentoFiscal modelo, int serie, CancellationToken ct)
    {
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var modeloByte = (short)modelo;

            // FOR UPDATE bloqueia apenas a row daquela tupla até o COMMIT.
            // Outros caixas/lojas seguem sem contention.
            var contador = await db.NotasFiscaisContadores
                .FromSqlInterpolated(
                    $@"SELECT * FROM nota_fiscal_contador
                       WHERE empresa_id = {empresaId}
                         AND loja_id = {lojaId}
                         AND modelo = {modeloByte}
                         AND serie = {serie}
                       FOR UPDATE")
                .SingleOrDefaultAsync(token);

            if (contador is null)
            {
                contador = new NotaFiscalContador
                {
                    EmpresaId = empresaId,
                    LojaId = lojaId,
                    Modelo = modelo,
                    Serie = serie,
                    UltimoNumero = 0,
                    AtualizadoEm = DateTime.UtcNow,
                };
                await db.NotasFiscaisContadores.AddAsync(contador, token);
                await db.SaveChangesAsync(token);
            }

            contador.UltimoNumero++;
            contador.AtualizadoEm = DateTime.UtcNow;

            if (contador.UltimoNumero > 999_999_999)
                throw new NumeracaoEsgotadaException(empresaId, lojaId, modelo, serie);

            await db.SaveChangesAsync(token);
            return contador.UltimoNumero;
        }, ct);
    }

    public async Task<int> ObterUltimoNumeroAsync(
        Guid empresaId, Guid lojaId, ModeloDocumentoFiscal modelo, int serie, CancellationToken ct)
    {
        var contador = await db.NotasFiscaisContadores
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId
                     && c.LojaId == lojaId
                     && c.Modelo == modelo
                     && c.Serie == serie)
            .SingleOrDefaultAsync(ct);

        return contador?.UltimoNumero ?? 0;
    }
}
