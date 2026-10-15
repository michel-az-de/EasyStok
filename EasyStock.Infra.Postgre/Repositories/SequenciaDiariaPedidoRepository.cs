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
    public async Task<int> ProximoAsync(Guid empresaId, DateOnly dia, CancellationToken ct = default)
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
