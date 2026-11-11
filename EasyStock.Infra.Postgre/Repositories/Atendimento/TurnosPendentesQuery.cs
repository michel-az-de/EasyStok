using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <inheritdoc cref="ITurnosPendentesQuery"/>
public sealed class TurnosPendentesQuery(EasyStockDbContext db) : ITurnosPendentesQuery
{
    public async Task<IReadOnlyList<TurnoPendente>> ListarAsync(DateTime desde, DateTime ate, CancellationToken ct = default) =>
        await (
            from c in db.AtendimentoConversas.IgnoreQueryFilters()
            // A coluna da última entrada (barata) corta antes da subconsulta da última palavra.
            where c.Situacao == SituacaoConversa.Automatica && c.UltimaMensagemEntradaEm >= desde
            let ultima = db.AtendimentoMensagens.IgnoreQueryFilters()
                .Where(m => m.EmpresaId == c.EmpresaId && m.ConversaId == c.Id)
                .Where(UltimaPalavraConversa.Conta)
                .OrderByDescending(m => m.EnviadaEm)
                .ThenByDescending(m => m.Id)
                .Select(m => new { m.Direcao, m.EnviadaEm })
                .FirstOrDefault()
            where ultima != null && ultima.Direcao == DirecaoMensagem.Entrada
                && ultima.EnviadaEm >= desde && ultima.EnviadaEm <= ate
            orderby ultima.EnviadaEm
            select new TurnoPendente(c.EmpresaId, c.Id))
            .ToListAsync(ct);
}
