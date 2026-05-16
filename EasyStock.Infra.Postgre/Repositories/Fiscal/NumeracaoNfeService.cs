using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Fiscal;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Fiscal;

/// <summary>
/// Implementacao de <see cref="INumeracaoNfeService"/> usando <c>SELECT FOR UPDATE</c>
/// sobre <c>empresa_configuracao_fiscal</c>. O lock acompanha a transacao do caller —
/// portanto este servico DEVE ser invocado dentro de
/// <see cref="EasyStock.Application.Ports.Output.Persistence.IUnitOfWork.ExecuteInTransactionAsync"/>.
///
/// <para>
/// Garantia: dois callers paralelos no mesmo <c>EmpresaId</c> serializam — o segundo
/// aguarda o primeiro commitar/rollback antes de ler o <c>ProximoNumeroNfce</c>. Em
/// ambiente de teste de carga (50 emissoes paralelas) deve produzir 50 numeros
/// distintos sem duplicata.
/// </para>
/// </summary>
public sealed class NumeracaoNfeService(EasyStockDbContext db) : INumeracaoNfeService
{
    public async Task<(short serie, long numero)> ReservarProximoNumeroAsync(Guid empresaId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT * FROM empresa_configuracao_fiscal
            WHERE "EmpresaId" = {0}
            FOR UPDATE
            """;

        var config = await db.EmpresaConfiguracoesFiscais
            .FromSqlRaw(sql, empresaId)
            .FirstOrDefaultAsync(ct);

        if (config is null)
            throw new RegraDeDominioVioladaException(
                $"Empresa {empresaId} nao possui configuracao fiscal. Criar via wizard antes de emitir.");

        if (!config.Habilitada)
            throw new RegraDeDominioVioladaException(
                $"Emissao fiscal nao habilitada para empresa {empresaId}. Habilitar via Admin > Config Fiscal.");

        var serie = config.SerieNfce;
        var numero = config.ReservarProximoNumero();

        // O caller commita a transacao — aqui apenas mutamos a entity rastreada
        return (serie, numero);
    }
}
