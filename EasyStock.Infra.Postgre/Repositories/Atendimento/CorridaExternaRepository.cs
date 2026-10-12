using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>I9.1: corridas externas. Arquivo próprio, fora de <c>EntregasRepositories.cs</c>.</summary>
public sealed class CorridaExternaRepository(EasyStockDbContext db) : ICorridaExternaRepository
{
    public Task AddAsync(CorridaExterna corrida, CancellationToken ct = default) =>
        db.CorridasExternas.AddAsync(corrida, ct).AsTask();

    public Task<CorridaExterna?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.CorridasExternas.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public async Task<IReadOnlyList<CorridaExterna>> ListarDaViagemAsync(Guid empresaId, Guid viagemId, CancellationToken ct = default) =>
        await db.CorridasExternas.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.ViagemId == viagemId)
            .OrderBy(c => c.RegistradoEm)
            .ToListAsync(ct);
}

/// <summary>
/// I9.1: custo das corridas contra o frete cobrado, por viagem. O frete cobrado é a linha de frete do
/// pedido de cada parada (<c>PedidoItem.EhLinhaDeFrete</c>); viagem desfeita fica fora.
/// </summary>
public sealed class CustoDeEntregaQuery(EasyStockDbContext db) : ICustoDeEntregaQuery
{
    public async Task<IReadOnlyList<CustoDeEntregaLinha>> ListarAsync(Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        var inicio = Utc(de);
        var fim = Utc(ate);

        var viagens = await db.Viagens.AsNoTracking()
            .Where(v => v.EmpresaId == empresaId && v.Situacao != SituacaoViagem.Desfeita
                        && v.CriadaEm >= inicio && v.CriadaEm < fim)
            .Select(v => new { v.Id, v.CriadaEm })
            .ToListAsync(ct);
        if (viagens.Count == 0) return [];

        var viagemIds = viagens.Select(v => v.Id).ToList();
        var paradas = await db.ParadasViagem.AsNoTracking()
            .Where(p => viagemIds.Contains(p.ViagemId))
            .Select(p => new { p.ViagemId, p.PedidoId })
            .ToListAsync(ct);

        // Pré-filtro no banco; a regra de "linha de frete" é a do domínio, aplicada em memória.
        var pedidoIds = paradas.Select(p => p.PedidoId).Distinct().ToList();
        var itens = await db.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && pedidoIds.Contains(p.Id))
            .SelectMany(p => p.Itens.Where(i => i.ProdutoId == null && i.CardapioItemId == null))
            .ToListAsync(ct);
        var fretePorPedido = itens.Where(i => i.EhLinhaDeFrete)
            .GroupBy(i => i.PedidoId)
            .ToDictionary(g => g.Key, g => (int)Math.Round(g.Sum(i => i.Subtotal) * 100m, MidpointRounding.AwayFromZero));

        var custoPorViagem = await db.CorridasExternas.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && viagemIds.Contains(c.ViagemId))
            .GroupBy(c => c.ViagemId)
            .Select(g => new { ViagemId = g.Key, Custo = g.Sum(c => c.CustoCentavos) })
            .ToDictionaryAsync(g => g.ViagemId, g => g.Custo, ct);

        var paradasPorViagem = paradas.ToLookup(p => p.ViagemId, p => p.PedidoId);
        return viagens.Select(v => new CustoDeEntregaLinha(
            v.Id, v.CriadaEm, paradasPorViagem[v.Id].Count(),
            paradasPorViagem[v.Id].Sum(pedidoId => fretePorPedido.GetValueOrDefault(pedidoId)),
            custoPorViagem.GetValueOrDefault(v.Id))).ToList();
    }

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}