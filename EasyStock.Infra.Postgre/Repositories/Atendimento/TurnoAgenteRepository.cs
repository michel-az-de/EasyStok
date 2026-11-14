using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class TurnoAgenteRepository(EasyStockDbContext db) : ITurnoAgenteRepository
{
    public Task AddAsync(TurnoAgente turno, CancellationToken ct = default) =>
        db.TurnosAgente.AddAsync(turno, ct).AsTask();

    public async Task<PaginaTurnosAgente> ListarAsync(Guid empresaId, FiltroTurnosAgente filtro, CancellationToken ct = default)
    {
        var query = db.TurnosAgente.AsNoTracking().Where(t => t.EmpresaId == empresaId);
        if (filtro.ConversaId is { } conversaId) query = query.Where(t => t.ConversaId == conversaId);
        if (filtro.Desfecho is { } desfecho) query = query.Where(t => t.Desfecho == desfecho);

        var total = await query.CountAsync(ct);
        var linhas = await query
            .OrderByDescending(t => t.CriadoEm).ThenByDescending(t => t.Id)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .Select(t => new
            {
                Turno = t,
                Pergunta = db.AtendimentoMensagens
                    .Where(m => m.EmpresaId == empresaId && m.Id == t.MensagemClienteId)
                    .Select(m => m.Texto ?? m.Transcricao).FirstOrDefault(),
                Resposta = db.AtendimentoMensagens
                    .Where(m => m.EmpresaId == empresaId && m.Id == t.MensagemRespostaId)
                    .Select(m => m.Texto).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return new PaginaTurnosAgente(
            linhas.Select(l => new TurnoAgenteLido(l.Turno.Id, l.Turno.ConversaId, l.Turno.CriadoEm, l.Turno.Modelo,
                l.Turno.TokensEntrada, l.Turno.TokensSaida, l.Turno.TokensCacheLido, l.Turno.CustoEstimado, l.Turno.Ferramentas,
                l.Turno.Passos, l.Turno.Desfecho, l.Turno.DuracaoMs, l.Pergunta, l.Resposta)).ToList(),
            total);
    }

    public async Task<SomaTurnosAgente> SomarAsync(Guid empresaId, DateTime deUtc, DateTime ateUtc, CancellationToken ct = default)
    {
        var soma = await db.TurnosAgente.AsNoTracking()
            .Where(t => t.EmpresaId == empresaId && t.CriadoEm >= deUtc && t.CriadoEm < ateUtc)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Turnos = g.Count(),
                Entrada = g.Sum(t => (long)t.TokensEntrada),
                Saida = g.Sum(t => (long)t.TokensSaida),
                CacheLido = g.Sum(t => (long)t.TokensCacheLido),
                Custo = g.Sum(t => t.CustoEstimado ?? 0m),
                SemPreco = g.Count(t => t.CustoEstimado == null),
            })
            .FirstOrDefaultAsync(ct);

        return soma is null
            ? SomaTurnosAgente.Vazia
            : new SomaTurnosAgente(soma.Turnos, soma.Entrada, soma.Saida, soma.CacheLido, soma.Custo, soma.SemPreco);
    }
}
