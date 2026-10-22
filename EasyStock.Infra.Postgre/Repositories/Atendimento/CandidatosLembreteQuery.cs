using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>
/// Consultas do avaliador de lembretes (S43). Cross-tenant: o host liga o bypass de RLS e
/// <c>IgnoreQueryFilters</c> tira o filtro global de tenant.
/// </summary>
public sealed class CandidatosLembreteQuery(EasyStockDbContext db) : ICandidatosLembreteQuery
{
    private static readonly string[] StatusAgendados =
    [
        StatusPedidoMapper.AguardandoPagamento, StatusPedidoMapper.AguardandoAprovacaoBaba,
        StatusPedidoMapper.AprovadoBaba, StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando,
        StatusPedidoMapper.Pronto, StatusPedidoMapper.SaiuParaEntrega,
    ];

    private static readonly string[] StatusComPreparoIniciado =
        [StatusPedidoMapper.Preparando, StatusPedidoMapper.Pronto, StatusPedidoMapper.SaiuParaEntrega];

    public async Task<IReadOnlyList<PedidoAgendadoLembrete>> ListarPedidosAgendadosAsync(CancellationToken ct = default)
    {
        var linhas = await (
            from p in db.Pedidos.IgnoreQueryFilters().AsNoTracking()
            where StatusAgendados.Contains(p.Status)
            let vaga = (
                from v in db.VagasOcupadas.IgnoreQueryFilters()
                join j in db.JanelasEntrega.IgnoreQueryFilters() on v.JanelaEntregaId equals j.Id
                join s in db.Storefronts.IgnoreQueryFilters() on j.StorefrontId equals s.Id
                where v.PedidoId == p.Id && v.LiberadoEm == null && s.EmpresaId == p.EmpresaId
                select new { v.DataEntrega, j.HoraInicio }).FirstOrDefault()
            where vaga != null || p.AgendadoParaEm != null
            let conversaId = db.AtendimentoConversas.IgnoreQueryFilters()
                .Where(c => c.EmpresaId == p.EmpresaId && c.PedidoEmAndamentoId == p.Id && c.Situacao != SituacaoConversa.Encerrada)
                .OrderBy(c => c.Id).Select(c => (Guid?)c.Id).FirstOrDefault()
            select new
            {
                p.EmpresaId, p.Id, p.ClienteNome, p.AgendadoParaEm, p.InicioPrevistoEm, Vaga = vaga, ConversaId = conversaId,
                PreparoIniciado = StatusComPreparoIniciado.Contains(p.Status),
            })
            .ToListAsync(ct);

        var empresas = linhas.Select(l => l.EmpresaId).Distinct().ToArray();
        var expedientes = await db.ExpedientesLoja.IgnoreQueryFilters().AsNoTracking()
            .Where(e => empresas.Contains(e.EmpresaId)).ToDictionaryAsync(e => e.EmpresaId, ct);
        return linhas.Select(l => new PedidoAgendadoLembrete(l.EmpresaId, l.Id, l.ClienteNome,
            l.Vaga is { } v ? HorarioBrasil.InstanteUtc(v.DataEntrega, v.HoraInicio) : l.AgendadoParaEm!.Value,
            l.InicioPrevistoEm, l.ConversaId, expedientes.GetValueOrDefault(l.EmpresaId), l.PreparoIniciado)).ToList();
    }

    // "Parado desde" = AlteradoEm: o pedido é carimbado ao entrar em AguardandoPagamento (checkout ou
    // desfazer pagamento), e não há coluna com o instante exato da transição.
    public async Task<IReadOnlyList<PedidoSemBaixa>> ListarPedidosSemBaixaAsync(DateTime paradoDesdeAntesDe, CancellationToken ct = default) =>
        await db.Pedidos.IgnoreQueryFilters()
            .Where(p => p.Status == StatusPedidoMapper.AguardandoPagamento && p.AlteradoEm < paradoDesdeAntesDe)
            .OrderBy(p => p.AlteradoEm)
            .Select(p => new PedidoSemBaixa(
                p.EmpresaId,
                p.Id,
                p.ClienteNome,
                db.AtendimentoConversas.IgnoreQueryFilters()
                    .Where(c => c.EmpresaId == p.EmpresaId && c.PedidoEmAndamentoId == p.Id && c.Situacao != SituacaoConversa.Encerrada)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefault()))
            .ToListAsync(ct);

    /// <summary>
    /// #1622-4: até onde a conversa <see cref="SituacaoConversa.Automatica"/> com o cliente esperando ainda vira aviso. O turno
    /// do agente (ou a escalada com ele desligado) roda em memória e se perde num deploy; uma semana cobre fim de semana e
    /// feriado com a loja fechada sem acordar conversa esquecida há meses no dia em que esta regra entra.
    /// </summary>
    public static readonly TimeSpan AutomaticaEsperandoAte = TimeSpan.FromDays(7);

    // Quem falou por último segue UltimaPalavraConversa (#1550), o mesmo critério da inbox.
    public async Task<IReadOnlyList<ConversaSemResposta>> ListarConversasSemRespostaAsync(DateTime entradaAntesDe, CancellationToken ct = default)
    {
        var automaticaDesde = entradaAntesDe - AutomaticaEsperandoAte;
        var linhas = await (
            from c in db.AtendimentoConversas.IgnoreQueryFilters()
            where c.Situacao == SituacaoConversa.Assumida || c.Situacao == SituacaoConversa.Automatica
            let ultima = db.AtendimentoMensagens.IgnoreQueryFilters()
                .Where(m => m.EmpresaId == c.EmpresaId && m.ConversaId == c.Id)
                .Where(UltimaPalavraConversa.Conta)
                .OrderByDescending(m => m.EnviadaEm)
                .ThenByDescending(m => m.Id)
                .Select(m => new { m.Id, m.Direcao, m.EnviadaEm })
                .FirstOrDefault()
            where ultima != null && ultima.Direcao == DirecaoMensagem.Entrada && ultima.EnviadaEm < entradaAntesDe
            where c.Situacao == SituacaoConversa.Assumida || ultima.EnviadaEm >= automaticaDesde
            // #1427: o SLA da loja vai junto; nulo quando a loja não gravou configuração (o avaliador usa o padrão).
            let sla = db.ConfiguracoesAtendimento.IgnoreQueryFilters()
                .Where(cfg => cfg.EmpresaId == c.EmpresaId)
                .Select(cfg => (int?)cfg.SlaRespostaMinutos)
                .FirstOrDefault()
            orderby ultima.EnviadaEm
            select new { c.EmpresaId, c.Id, MensagemId = ultima.Id, c.ContatoNome, c.AssumidaPorUsuarioId, ultima.EnviadaEm, Sla = sla })
            .ToListAsync(ct);

        var empresas = linhas.Select(l => l.EmpresaId).Distinct().ToArray();
        var expedientes = await db.ExpedientesLoja.IgnoreQueryFilters().AsNoTracking()
            .Where(e => empresas.Contains(e.EmpresaId)).ToDictionaryAsync(e => e.EmpresaId, ct);
        return linhas.Select(l => new ConversaSemResposta(
            l.EmpresaId, l.Id, l.MensagemId, l.ContatoNome, l.AssumidaPorUsuarioId, l.EnviadaEm, l.Sla,
            expedientes.GetValueOrDefault(l.EmpresaId))).ToList();
    }
}
