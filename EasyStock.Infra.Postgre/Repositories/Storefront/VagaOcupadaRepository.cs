using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// EF Repository de <see cref="VagaOcupada"/>. <see cref="OcuparAsync"/> faz INSERT
/// atômico via SQL raw (necessário porque EF Core LINQ não expressa
/// "INSERT ... SELECT WHERE COUNT &lt; capacidade" — ADR-0014 §Solução 1).
/// </summary>
public sealed class VagaOcupadaRepository(EasyStockDbContext db) : IVagaOcupadaRepository
{
    public Task<VagaOcupada?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.VagasOcupadas.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<VagaOcupada> OcuparAsync(
        Guid janelaEntregaId,
        DateOnly dataEntrega,
        Guid pedidoId,
        CancellationToken ct = default)
    {
        // INSERT atômico condicionado: só insere se COUNT atual < CapacidadeMaxima da janela.
        // RETURNING garante que sabemos o que foi inserido. Se 0 linhas, janela cheia.
        const string sql = @"
            INSERT INTO vaga_ocupada (""Id"", ""JanelaEntregaId"", ""DataEntrega"", ""PedidoId"", ""OcupadoEm"")
            SELECT @id, @janelaId, @data, @pedidoId, @ocupadoEm
            WHERE (
                SELECT COUNT(*) FROM vaga_ocupada
                WHERE ""JanelaEntregaId"" = @janelaId
                  AND ""DataEntrega"" = @data
                  AND ""LiberadoEm"" IS NULL
            ) < (
                SELECT ""CapacidadeMaxima"" FROM janela_entrega WHERE ""Id"" = @janelaId
            )
            RETURNING ""Id"";";

        var novoId = Guid.NewGuid();
        var agora = DateTime.UtcNow;

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = novoId });
        cmd.Parameters.Add(new NpgsqlParameter("@janelaId", NpgsqlDbType.Uuid) { Value = janelaEntregaId });
        cmd.Parameters.Add(new NpgsqlParameter("@data", NpgsqlDbType.Date) { Value = dataEntrega });
        cmd.Parameters.Add(new NpgsqlParameter("@pedidoId", NpgsqlDbType.Uuid) { Value = pedidoId });
        cmd.Parameters.Add(new NpgsqlParameter("@ocupadoEm", NpgsqlDbType.TimestampTz) { Value = agora });

        var inserted = await cmd.ExecuteScalarAsync(ct);

        if (inserted is null)
            throw new JanelaSemVagasException(
                $"Janela {janelaEntregaId} esgotada para {dataEntrega:yyyy-MM-dd}.");

        // Carrega a entity recém-criada pra retornar ao caller (state tracking).
        var entity = await db.VagasOcupadas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == novoId, ct);

        return entity ?? throw new InvalidOperationException(
            $"VagaOcupada {novoId} inserida mas não encontrada no DbContext — race condition inesperada.");
    }

    public async Task<bool> LiberarPorPedidoAsync(Guid pedidoId, string motivo, CancellationToken ct = default)
    {
        var vaga = await db.VagasOcupadas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.PedidoId == pedidoId && v.LiberadoEm == null, ct);

        if (vaga is null) return false;

        vaga.Liberar(motivo);
        return true;
    }

    public Task<int> ContarAtivasPorJanelaDataAsync(
        Guid janelaEntregaId,
        DateOnly dataEntrega,
        CancellationToken ct = default) =>
        db.VagasOcupadas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(v =>
                v.JanelaEntregaId == janelaEntregaId
                && v.DataEntrega == dataEntrega
                && v.LiberadoEm == null, ct);

    public async Task<IReadOnlyList<VagaOcupada>> GetOrfasAsync(CancellationToken ct = default)
    {
        // Vagas ativas (LiberadoEm IS NULL) cujo pedido foi cancelado/entregue ou não existe.
        // Cinto+suspensório ADR-0014 §Solução 4.
        var orfas = await db.VagasOcupadas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.LiberadoEm == null
                && !db.Pedidos.IgnoreQueryFilters().Any(p =>
                    p.Id == v.PedidoId
                    && p.Status != StatusPedidoMapper.Cancelado
                    && p.Status != StatusPedidoMapper.Entregue))
            .ToListAsync(ct);

        return orfas;
    }
}
