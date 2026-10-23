using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Número do dia do pedido (S53) por upsert atômico: o <c>ON CONFLICT ... DO UPDATE</c> trava a linha do dia,
/// então chamadas simultâneas saem em fila (1, 2, 3) sem repetir nem falhar. Roda na conexão do contexto, dentro
/// da transação aberta pelo use case (rollback devolve o número) e sob o RLS do tenant.
/// </summary>
public sealed class SequenciaDiariaPedidoRepository(EasyStockDbContext db) : ISequenciaDiariaPedido
{
    /// <summary>
    /// E12 (#1641-4): dentro da transação do pagamento, o upsert roda num savepoint. Se falhar, só ele é desfeito e a
    /// exceção sobe para o numerador, que deixa o pedido sem número; a transação do pagamento segue utilizável.
    /// </summary>
    private const string Savepoint = "numero_do_dia";

    public async Task<int> ProximoAsync(Guid empresaId, DateOnly dia, CancellationToken ct = default)
    {
        var transacao = db.Database.CurrentTransaction;
        if (transacao is null) return await UpsertAsync(empresaId, dia, ct);
        await transacao.CreateSavepointAsync(Savepoint, ct);
        try
        {
            var numero = await UpsertAsync(empresaId, dia, ct);
            await transacao.ReleaseSavepointAsync(Savepoint, ct);
            return numero;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await transacao.RollbackToSavepointAsync(Savepoint, CancellationToken.None);
            throw;
        }
    }

    private async Task<int> UpsertAsync(Guid empresaId, DateOnly dia, CancellationToken ct)
    {
        var numeros = await db.Database
            .SqlQuery<int>($"""
                INSERT INTO sequencias_pedido_dia ("EmpresaId", "Data", "Ultimo")
                VALUES ({empresaId}, {dia}, 1)
                ON CONFLICT ("EmpresaId", "Data") DO UPDATE SET "Ultimo" = sequencias_pedido_dia."Ultimo" + 1
                RETURNING "Ultimo" AS "Value"
                """)
            .ToListAsync(ct);
        return numeros.Single();
    }
}
