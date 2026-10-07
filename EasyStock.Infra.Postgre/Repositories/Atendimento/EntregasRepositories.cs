using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class EntregadorRepository(EasyStockDbContext db) : IEntregadorRepository
{
    public Task AddAsync(Entregador entregador, CancellationToken ct = default) =>
        db.Entregadores.AddAsync(entregador, ct).AsTask();

    public Task<Entregador?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.Entregadores.FirstOrDefaultAsync(e => e.EmpresaId == empresaId && e.Id == id, ct);

    public async Task<IReadOnlyList<Entregador>> ListarAsync(Guid empresaId, bool incluirInativos, CancellationToken ct = default)
    {
        var query = db.Entregadores.AsNoTracking().Where(e => e.EmpresaId == empresaId);
        if (!incluirInativos) query = query.Where(e => e.Ativo);
        return await query.OrderBy(e => e.Nome).ToListAsync(ct);
    }
}

public sealed class ViagemRepository(EasyStockDbContext db) : IViagemRepository
{
    public Task AddAsync(Viagem viagem, CancellationToken ct = default) =>
        db.Viagens.AddAsync(viagem, ct).AsTask();

    public Task<Viagem?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.Viagens.Include(v => v.Paradas).FirstOrDefaultAsync(v => v.EmpresaId == empresaId && v.Id == id, ct);

    public async Task<IReadOnlyList<Viagem>> ListarAsync(Guid empresaId, SituacaoViagem? situacao, int limite, CancellationToken ct = default)
    {
        var query = db.Viagens.AsNoTracking().Include(v => v.Paradas).Where(v => v.EmpresaId == empresaId);
        if (situacao is { } s) query = query.Where(v => v.Situacao == s);
        return await query.OrderByDescending(v => v.CriadaEm).Take(Math.Clamp(limite, 1, 500)).ToListAsync(ct);
    }

    public Task<bool> PedidoEmViagemAtivaAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default) =>
        db.Viagens.AnyAsync(v => v.EmpresaId == empresaId
            && (v.Situacao == SituacaoViagem.Montando || v.Situacao == SituacaoViagem.EmRota)
            && v.Paradas.Any(p => p.PedidoId == pedidoId), ct);

    public Task RegistrarParadaNovaAsync(ParadaViagem parada, CancellationToken ct = default)
    {
        db.ParadasViagem.Add(parada);
        return Task.CompletedTask;
    }
}

public sealed class ChamadoEntregadorRepository(EasyStockDbContext db) : IChamadoEntregadorRepository
{
    public Task AddAsync(ChamadoEntregador chamado, CancellationToken ct = default) =>
        db.ChamadosEntregador.AddAsync(chamado, ct).AsTask();

    public Task<ChamadoEntregador?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.ChamadosEntregador.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public async Task<IReadOnlyList<ChamadoEntregador>> ListarAsync(Guid empresaId, bool apenasAbertos, int limite, CancellationToken ct = default)
    {
        var query = db.ChamadosEntregador.AsNoTracking().Where(c => c.EmpresaId == empresaId);
        if (apenasAbertos) query = query.Where(c => c.Situacao == SituacaoChamadoEntregador.Aberto);
        return await query.OrderByDescending(c => c.AbertoEm).Take(Math.Clamp(limite, 1, 500)).ToListAsync(ct);
    }
}

public sealed class EntregasPorBairroQuery(EasyStockDbContext db) : IEntregasPorBairroQuery
{
    public async Task<IReadOnlyList<PedidoEntregueLinha>> ListarEntreguesAsync(Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        var inicio = Utc(de);
        var fim = Utc(ate);
        // Total é o VO Dinheiro com conversão para numeric: projeta a entidade e soma em memória.
        var linhas = await db.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId
                && p.Status == StatusPedidoMapper.Entregue
                && p.EntreguEm >= inicio && p.EntreguEm < fim)
            .Select(p => new { Bairro = p.Cliente != null ? p.Cliente.Bairro : null, p.Total })
            .ToListAsync(ct);
        return linhas.Select(l => new PedidoEntregueLinha(l.Bairro, l.Total.Valor)).ToList();
    }

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
