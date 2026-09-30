using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Leitura do início previsto do pedido (S21). <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).
/// Item do cardápio vale o tempo próprio (ou o padrão); item sem cardápio (frete, avulso) não entra, e
/// pedido sem nenhum item do cardápio vale o padrão da empresa.
/// </summary>
public sealed class PrazoPreparoPedidoQueries(EasyStockDbContext db) : IPrazoPreparoPedidoQueries
{
    public async Task<PrazoPreparoPedidoLeitura?> ObterAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default)
    {
        var existe = await db.Pedidos.AsNoTracking()
            .AnyAsync(p => p.Id == pedidoId && p.EmpresaId == empresaId, ct);
        if (!existe) return null;

        var janela = await (
                from v in db.VagasOcupadas.AsNoTracking()
                join j in db.JanelasEntrega.AsNoTracking() on v.JanelaEntregaId equals j.Id
                where v.PedidoId == pedidoId && v.LiberadoEm == null
                orderby v.OcupadoEm descending
                select new { v.DataEntrega, j.HoraInicio })
            .FirstOrDefaultAsync(ct);

        var tempos = await (
                from i in db.Set<PedidoItem>().AsNoTracking()
                join p in db.Pedidos.AsNoTracking() on i.PedidoId equals p.Id
                join c in db.CardapioItens.AsNoTracking() on i.CardapioItemId equals (Guid?)c.Id
                where i.PedidoId == pedidoId && p.EmpresaId == empresaId
                select c.TempoPreparoMinutos)
            .ToListAsync(ct);
        if (tempos.Count == 0) tempos.Add(null);

        var config = await db.ConfiguracoesAtendimento.AsNoTracking()
                         .FirstOrDefaultAsync(c => c.EmpresaId == empresaId, ct)
                     ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);

        return new PrazoPreparoPedidoLeitura(
            janela?.DataEntrega,
            janela?.HoraInicio,
            tempos,
            config.TempoPreparoPadraoMinutos,
            config.RespiroMinutos);
    }
}
