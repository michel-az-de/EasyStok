using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Ia;
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
                l.Turno.Passos, l.Turno.Desfecho, l.Turno.DuracaoMs, l.Pergunta, l.Resposta, l.Turno.Motivo)).ToList(),
            total);
    }

    public async Task<SomaTurnosAgente> SomarAsync(Guid empresaId, DateTime deUtc, DateTime ateUtc, CancellationToken ct = default)
    {
        var soma = await db.TurnosAgente.AsNoTracking()
            .Where(t => t.EmpresaId == empresaId && t.CriadoEm >= deUtc && t.CriadoEm < ateUtc)
            // #3175: turno de portão não chama o modelo; fica no histórico e fora do uso do mês.
            .Where(t => t.Motivo == null || !t.Motivo.StartsWith("portao:"))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Turnos = g.Count(),
                Entrada = g.Sum(t => (long)t.TokensEntrada),
                Saida = g.Sum(t => (long)t.TokensSaida),
                CacheLido = g.Sum(t => (long)t.TokensCacheLido),
                Custo = g.Sum(t => t.CustoEstimado ?? 0m),
                SemPreco = g.Count(t => t.CustoEstimado == null),
                SemIa = g.Count(t => t.Desfecho == DesfechoTurnoAgente.SemIa),
                ComTokens = g.Count(t => t.TokensEntrada + t.TokensSaida + t.TokensCacheLido > 0),
            })
            .FirstOrDefaultAsync(ct);

        var turnos = soma is null
            ? SomaTurnosAgente.Vazia
            : new SomaTurnosAgente(soma.Turnos, soma.Entrada, soma.Saida, soma.CacheLido, soma.Custo, soma.SemPreco, soma.SemIa,
                soma.ComTokens);

        // #3370 (D2 da #3271): piso. Cada chamada ao modelo do Assistente fica em chamada_ia, ligada ao turno por
        // turno_agente_id; se o turno caiu depois de chamar o modelo, os tokens dele só existem lá. Fora ficam a sugestão de
        // resposta para a equipe (finalidade Atendimento, sem turno) e as outras finalidades. A escrita de cache entra na
        // entrada, como o turno a soma (#2862). Falha de banco propaga, como na soma dos turnos: o controle de custo não abre.
        // Fica neste repositório, e não em IRegistroChamadaIa, para que nenhum host monte a cota sem o piso e o construtor de
        // PortoesDoAssistente siga como o ControleDeCustoDoAssistenteTests exige. Não é a soma de SomarMesAsync (todas as
        // finalidades, entrada sem a escrita de cache): aqui é só o consumo do Assistente, na conta do turno. O desfecho da
        // chamada não filtra: o que foi registrado com tokens conta, mesmo que tenha terminado em erro.
        var chamadas = await db.ChamadasIa.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.CriadoEm >= deUtc && c.CriadoEm < ateUtc)
            .Where(c => c.Finalidade == FinalidadeIa.Atendimento && c.TurnoAgenteId != null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Entrada = g.Sum(c => (long)c.TokensEntrada + c.TokensCacheEscrito),
                Saida = g.Sum(c => (long)c.TokensSaida),
                CacheLido = g.Sum(c => (long)c.TokensCacheLido),
            })
            .FirstOrDefaultAsync(ct);

        return chamadas is null ? turnos : turnos.ComPisoDasChamadas(chamadas.Entrada, chamadas.Saida, chamadas.CacheLido);
    }
}
