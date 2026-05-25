using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// EF Repository de <see cref="VagaOcupada"/>. <see cref="OcuparAsync"/> serializa
/// ocupações da mesma janela via <c>pg_advisory_xact_lock(hashtext(janelaId))</c>
/// (auto-release no commit), evitando race condition em READ COMMITTED entre o
/// COUNT e o INSERT (ADR-0014 §Solução 1, refinada).
///
/// <para>
/// Roda dentro de transação ambiente quando existe (caller controla commit);
/// senão abre uma local via <see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/>
/// — compatível com <c>EnableRetryOnFailure</c>.
/// </para>
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
        // Se já existe tx ambiente, reusa (caller é dono do commit). Senão envolve
        // em ExecutionStrategy + tx local — ExecutionStrategy é obrigatório quando
        // EnableRetryOnFailure está ligado (Npgsql) e abrimos transação explícita.
        if (db.Database.CurrentTransaction is not null)
            return await OcuparDentroDeTxAsync(janelaEntregaId, dataEntrega, pedidoId, ct);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            state: (janelaEntregaId, dataEntrega, pedidoId),
            operation: async (_, s, ct2) =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct2);
                var vaga = await OcuparDentroDeTxAsync(s.janelaEntregaId, s.dataEntrega, s.pedidoId, ct2);
                await tx.CommitAsync(ct2);
                return vaga;
            },
            verifySucceeded: null,
            cancellationToken: ct);
    }

    private async Task<VagaOcupada> OcuparDentroDeTxAsync(
        Guid janelaEntregaId,
        DateOnly dataEntrega,
        Guid pedidoId,
        CancellationToken ct)
    {
        // Advisory lock serializa ocupações da mesma janela. hashtext(uuid) mapeia
        // pra int4 — colisões entre janelas distintas só geram serialização extra,
        // nunca corretude (worst case: dois locks de janelas diferentes contendem
        // por compartilhar hash). O lock é liberado automaticamente no commit/rollback.
        var janelaIdTexto = janelaEntregaId.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({janelaIdTexto}))",
            ct);

        var capacidade = await db.JanelasEntrega
            .IgnoreQueryFilters()
            .Where(j => j.Id == janelaEntregaId)
            .Select(j => (int?)j.CapacidadeMaxima)
            .FirstOrDefaultAsync(ct);

        if (capacidade is null)
            throw new InvalidOperationException(
                $"Janela {janelaEntregaId} não encontrada.");

        var atual = await db.VagasOcupadas
            .IgnoreQueryFilters()
            .CountAsync(v =>
                v.JanelaEntregaId == janelaEntregaId
                && v.DataEntrega == dataEntrega
                && v.LiberadoEm == null, ct);

        if (atual >= capacidade.Value)
            throw new JanelaSemVagasException(
                $"Janela {janelaEntregaId} esgotada para {dataEntrega:yyyy-MM-dd}.");

        var vaga = VagaOcupada.Ocupar(janelaEntregaId, dataEntrega, pedidoId);

        try
        {
            await db.VagasOcupadas.AddAsync(vaga, ct);
            await db.SaveChangesAsync(ct);
            return vaga;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // uq_vaga_ativa_por_pedido — pedido já tem vaga ativa (em qualquer janela/data).
            // Não é race da capacidade desta janela (advisory lock cobre isso); é violação
            // de invariante de business. Caller deve liberar a anterior antes de ocupar.
            db.Entry(vaga).State = EntityState.Detached;
            throw new RegraDeDominioVioladaException(
                $"Pedido {pedidoId} já tem vaga ativa — libere/cancele a anterior antes de ocupar nova.",
                ex);
        }
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505";
}
