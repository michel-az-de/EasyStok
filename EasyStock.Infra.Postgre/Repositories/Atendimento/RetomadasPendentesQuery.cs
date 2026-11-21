using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <inheritdoc cref="IRetomadasPendentesQuery"/>
public sealed class RetomadasPendentesQuery(EasyStockDbContext db) : IRetomadasPendentesQuery
{
    public async Task<IReadOnlyList<TurnoPendente>> ListarAsync(DateTime desde, DateTime ate, CancellationToken ct = default)
    {
        var candidatas = await (
            from c in db.AtendimentoConversas.IgnoreQueryFilters().AsNoTracking()
            // As colunas da conversa (baratas) cortam antes das subconsultas de mensagem.
            where c.Situacao == SituacaoConversa.Assumida
                && (c.Canal == CanalConversa.WhatsApp || c.Canal == CanalConversa.ChatSite)
                && !c.VinculoEmRevisao
                && c.UltimaMensagemEntradaEm >= desde && c.UltimaMensagemEntradaEm <= ate
            where db.ConfiguracoesAtendimento.IgnoreQueryFilters().Any(k => k.EmpresaId == c.EmpresaId && k.Ativo)
            where c.ClienteId == null || !db.Clientes.IgnoreQueryFilters().Any(p => p.EmpresaId == c.EmpresaId && p.Id == c.ClienteId
                && (p.Bloqueado || p.AtendimentoAutomaticoPausadoEm != null))
            let entrada = db.AtendimentoMensagens.IgnoreQueryFilters()
                .Where(m => m.EmpresaId == c.EmpresaId && m.ConversaId == c.Id && m.Autor == AutorMensagem.Cliente)
                .OrderByDescending(m => m.EnviadaEm)
                .ThenByDescending(m => m.Id)
                .Select(m => new { m.Id, m.EnviadaEm })
                .FirstOrDefault()
            where entrada != null
            // A equipe já respondeu a essa mensagem: o atendimento é dela.
            where !db.AtendimentoMensagens.IgnoreQueryFilters().Any(m => m.EmpresaId == c.EmpresaId && m.ConversaId == c.Id
                && m.EnviadaEm > entrada.EnviadaEm && m.Status != StatusMensagem.Falhou
                && (m.Autor == AutorMensagem.Dona || m.Autor == AutorMensagem.Atendente))
            // O robô respondeu e não passou para a equipe: ninguém espera, a equipe só assumiu a conversa.
            where !db.AtendimentoMensagens.IgnoreQueryFilters().Any(m => m.EmpresaId == c.EmpresaId && m.ConversaId == c.Id
                    && m.EnviadaEm > entrada.EnviadaEm && m.Status != StatusMensagem.Falhou && m.Autor == AutorMensagem.Agente)
                || db.AtendimentoMensagens.IgnoreQueryFilters().Any(m => m.EmpresaId == c.EmpresaId && m.ConversaId == c.Id
                    && m.EnviadaEm > entrada.EnviadaEm && m.Autor == AutorMensagem.Sistema
                    && m.Texto!.StartsWith(EscalarConversaUseCase.PrefixoMotivo))
            orderby entrada.EnviadaEm
            select new { c.EmpresaId, ConversaId = c.Id, EntradaId = entrada.Id, c.ContextoJson })
            .ToListAsync(ct);

        // A guarda por mensagem mora no JSON do contexto: lida aqui, sobre as poucas candidatas.
        return candidatas
            .Where(c => !Conversa.RetomadaPorTempoDecidida(c.ContextoJson, c.EntradaId))
            .Select(c => new TurnoPendente(c.EmpresaId, c.ConversaId))
            .ToList();
    }
}
