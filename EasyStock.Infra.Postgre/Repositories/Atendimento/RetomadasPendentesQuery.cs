using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
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
                // Assumir e transferir movem a última atividade: quem acabou de pegar tem o prazo inteiro.
                && c.UltimaMensagemEm <= ate
            where db.ConfiguracoesAtendimento.IgnoreQueryFilters().Any(k => k.EmpresaId == c.EmpresaId && k.Ativo)
            where c.ClienteId == null || !db.Clientes.IgnoreQueryFilters().Any(p => p.EmpresaId == c.EmpresaId && p.Id == c.ClienteId
                && (p.Bloqueado || p.AtendimentoAutomaticoPausadoEm != null))
            let entrada = db.AtendimentoMensagens.IgnoreQueryFilters()
                // Mesmo corte de RetomadaDoAssistente.EhEntradaQueEsperaResposta: reação, figurinha e toque em botão de
                // ação não esperam resposta.
                .Where(m => m.EmpresaId == c.EmpresaId && m.ConversaId == c.Id && m.Autor == AutorMensagem.Cliente
                    && m.TipoConteudo != TipoConteudoMensagem.Outro
                    && (m.BotaoId == null || !m.BotaoId.StartsWith(RoteadorAcoesBotao.Prefixo)))
                .OrderByDescending(m => m.EnviadaEm)
                .ThenByDescending(m => m.Id)
                .Select(m => new { m.Id, m.EnviadaEm })
                .FirstOrDefault()
            // A faixa vale para a entrada que espera: reação recente não traz de volta pergunta velha.
            where entrada != null && entrada.EnviadaEm >= desde
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
