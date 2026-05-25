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
            SELECT *, xmin FROM empresa_configuracao_fiscal
            WHERE "EmpresaId" = {0}
            FOR UPDATE
            """;

        // Guarda defensiva: SELECT FOR UPDATE só funciona dentro de transação explícita.
        // Sem isso, Postgres devolve o registro mas o lock NÃO persiste — duas emissões
        // paralelas pegam o mesmo número. Falhamos rápido (ao invés de duplicar números).
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "ReservarProximoNumeroAsync deve ser chamado dentro de IUnitOfWork.ExecuteInTransactionAsync — sem transação ativa, o FOR UPDATE é descartado e ocorrem números duplicados.");

        // .IgnoreQueryFilters(): impede que o filtro global de tenant envolva o raw
        // numa subquery. O raw ja filtra por "EmpresaId" — isolamento preservado.
        // Critico aqui: o wrap em subquery pode levar o Postgres a re-planejar o
        // FOR UPDATE de forma indesejada e, sob carga paralela, abrir janela de
        // duplicacao de numero fiscal. Tirar o filtro EF e defesa-em-profundidade.
        var config = await db.EmpresaConfiguracoesFiscais
            .FromSqlRaw(sql, empresaId)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct);

        if (config is null)
            throw new RegraDeDominioVioladaException(
                $"Empresa {empresaId} não possui configuração fiscal. Cadastre a configuração antes de emitir.");

        if (!config.Habilitada)
            throw new RegraDeDominioVioladaException(
                "Emissão fiscal não habilitada para este tenant. Habilite via POST /api/configuracao-fiscal/habilitar.");

        var serie = config.SerieNfce;
        var numero = config.ReservarProximoNumero();

        // O caller commita a transação — aqui apenas mutamos a entity rastreada.
        return (serie, numero);
    }
}
