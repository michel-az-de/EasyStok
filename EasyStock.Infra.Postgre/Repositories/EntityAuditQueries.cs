using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre do read model de auditoria universal de entidades (F7).
/// </summary>
public sealed class EntityAuditQueries(EasyStockDbContext db) : IEntityAuditQueries
{
    public async Task<(IReadOnlyList<EntityAuditEntry> Items, int Total)> PorEntidadeAsync(
        Guid empresaId, string tipoEntidade, Guid entidadeId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.EntityAlteracoes.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                     && a.TipoEntidade == tipoEntidade
                     && a.EntidadeId == entidadeId)
            .OrderByDescending(a => a.AlteradoEm);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new EntityAuditEntry(
                a.Id, a.Acao, a.Campo, a.ValorAntigo, a.ValorNovo,
                a.AlteradoPorNome, a.Origem, a.AlteradoEm, a.PiiCriptografado != null))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<ClientTimelineEntry> Items, int Total)> TimelineClienteAsync(
        Guid empresaId, Guid clienteId, int page, int pageSize, CancellationToken ct = default)
    {
        var clienteEntries = db.EntityAlteracoes.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                     && a.TipoEntidade == "Cliente"
                     && a.EntidadeId == clienteId);

        // Pedidos do cliente — subquery no banco (sem materializar IDs em memória)
        var pedidoIdsQuery = db.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.ClienteId == clienteId)
            .Select(p => p.Id);

        var pedidoEntries = db.EntityAlteracoes.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                     && (a.TipoEntidade == "Pedido" || a.TipoEntidade == "PedidoItem" || a.TipoEntidade == "PedidoPagamento")
                     && pedidoIdsQuery.Contains(a.EntidadeId));

        var union = clienteEntries.Union(pedidoEntries)
            .OrderByDescending(a => a.AlteradoEm);

        var total = await union.CountAsync(ct);
        var items = await union
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ClientTimelineEntry(
                a.Id, a.TipoEntidade, a.EntidadeId, a.Acao, a.Campo, a.ValorAntigo, a.ValorNovo,
                a.AlteradoPorNome, a.Origem, a.AlteradoEm))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<EntityAuditTipoContagem>> ResumoPorTipoAsync(
        Guid empresaId, CancellationToken ct = default)
        => await db.EntityAlteracoes.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId)
            .GroupBy(a => a.TipoEntidade)
            .Select(g => new EntityAuditTipoContagem(g.Key, g.Count()))
            .ToListAsync(ct);
}
