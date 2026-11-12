using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class VinculoClienteConversaRepository(EasyStockDbContext db) : IVinculoClienteConversaRepository
{
    public async Task<IReadOnlyList<PedidoDoVinculo>> ListarPedidosAsync(Guid empresaId, Guid conversaId, CancellationToken ct)
    {
        var conversa = await db.AtendimentoConversas.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.Id == conversaId)
            .Select(c => new { c.PedidoEmAndamentoId, c.PedidosHistoricoIds }).SingleOrDefaultAsync(ct);
        if (conversa is null) return [];
        var ids = conversa.PedidosHistoricoIds.ToList();
        if (conversa.PedidoEmAndamentoId is { } atual) ids.Add(atual);
        ids.AddRange(await db.LinksCardapioConversa.Where(l => l.EmpresaId == empresaId && l.ConversaId == conversaId
            && l.PedidoRegistradoId != null).Select(l => l.PedidoRegistradoId!.Value).ToListAsync(ct));
        ids.AddRange(await db.Set<EasyStock.Domain.Entities.Pagamentos.CobrancaPedido>()
            .Where(c => c.EmpresaId == empresaId && c.ConversaId == conversaId).Select(c => c.PedidoId).ToListAsync(ct));
        var pedidos = await db.Pedidos.AsNoTracking().Where(p => p.EmpresaId == empresaId && ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Status, p.Total }).ToListAsync(ct);
        return pedidos.Select(p => new PedidoDoVinculo(p.Id, p.Status, p.Total.Valor)).ToList();
    }

    public async Task<IReadOnlyList<CandidatoClienteConversa>> ListarCandidatosAsync(Guid empresaId,
        IReadOnlyList<string> telefones, IReadOnlyList<string> hashes, CancellationToken ct) =>
        await db.Clientes.AsNoTracking().Where(c => c.EmpresaId == empresaId
            && ((c.TelefoneHash != null && hashes.Contains(c.TelefoneHash))
                || (c.Telefone != null && telefones.Contains(c.Telefone)) || c.Telefones.Any(t => telefones.Contains(t.Numero))))
            .OrderBy(c => c.Nome).ThenBy(c => c.Id)
            .Select(c => new CandidatoClienteConversa(c.Id, c.Nome, c.Telefone, c.OrderCount == 0, c.Bloqueado))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AlteracaoVinculoConversa>> ListarHistoricoAsync(Guid empresaId, Guid conversaId, CancellationToken ct) =>
        await db.Set<AlteracaoVinculoConversa>().AsNoTracking().Where(a => a.EmpresaId == empresaId && a.ConversaId == conversaId)
            .OrderByDescending(a => a.Revisao).Take(100).ToListAsync(ct);

    public Task<AlteracaoVinculoConversa?> ObterTentativaAsync(Guid empresaId, Guid conversaId, Guid requestId, CancellationToken ct) =>
        db.Set<AlteracaoVinculoConversa>().AsNoTracking().SingleOrDefaultAsync(a => a.EmpresaId == empresaId
            && a.ConversaId == conversaId && a.RequestId == requestId, ct);

    public Task AddAsync(AlteracaoVinculoConversa alteracao, CancellationToken ct) => db.Set<AlteracaoVinculoConversa>().AddAsync(alteracao, ct).AsTask();

    public async Task ExpirarLinksAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct) =>
        await db.LinksCardapioConversa.Where(l => l.EmpresaId == empresaId && l.ConversaId == conversaId && l.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiraEm, agora), ct);

    public async Task EncerrarSessoesChatAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct)
    {
        var sessoes = await db.SessoesChatSite.Where(s => s.EmpresaId == empresaId && s.ConversaId == conversaId).ToListAsync(ct);
        foreach (var sessao in sessoes) sessao.Encerrar(agora);
    }
}
