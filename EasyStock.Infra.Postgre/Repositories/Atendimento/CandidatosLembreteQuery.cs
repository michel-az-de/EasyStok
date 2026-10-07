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

    // Nota interna do sistema (ex.: "escalado para a dona") não é resposta ao cliente: fica de fora ao
    // procurar a última mensagem da conversa.
    public async Task<IReadOnlyList<ConversaSemResposta>> ListarConversasSemRespostaAsync(DateTime entradaAntesDe, CancellationToken ct = default)
    {
        var linhas = await (
            from c in db.AtendimentoConversas.IgnoreQueryFilters()
            where c.Situacao == SituacaoConversa.Assumida
            let ultima = db.AtendimentoMensagens.IgnoreQueryFilters()
                .Where(m => m.EmpresaId == c.EmpresaId && m.ConversaId == c.Id && m.Autor != AutorMensagem.Sistema)
                .OrderByDescending(m => m.EnviadaEm)
                .Select(m => new { m.Id, m.Direcao, m.EnviadaEm })
                .FirstOrDefault()
            where ultima != null && ultima.Direcao == DirecaoMensagem.Entrada && ultima.EnviadaEm < entradaAntesDe
            // #1427: o SLA da loja vai junto; nulo quando a loja não gravou configuração (o avaliador usa o padrão).
            let sla = db.ConfiguracoesAtendimento.IgnoreQueryFilters()
                .Where(cfg => cfg.EmpresaId == c.EmpresaId)
                .Select(cfg => (int?)cfg.SlaRespostaMinutos)
                .FirstOrDefault()
            orderby ultima.EnviadaEm
            select new { c.EmpresaId, c.Id, MensagemId = ultima.Id, c.ContatoNome, c.AssumidaPorUsuarioId, ultima.EnviadaEm, Sla = sla })
            .ToListAsync(ct);

        return linhas.Select(l => new ConversaSemResposta(
            l.EmpresaId, l.Id, l.MensagemId, l.ContatoNome, l.AssumidaPorUsuarioId, l.EnviadaEm, l.Sla)).ToList();
    }
}
