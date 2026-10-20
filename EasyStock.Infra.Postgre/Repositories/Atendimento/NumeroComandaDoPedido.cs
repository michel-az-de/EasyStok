using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>
/// #1883: a "Comanda N" do pedido é o número da conversa dona dele, pela mesma regra de
/// <see cref="ConversaRepository.ObterIdPorPedidoAsync"/>: a conversa com o pedido em andamento ou a que
/// gerou o link do cardápio que o registrou. Uma consulta para a página inteira, nunca uma por pedido;
/// com mais de uma conversa para o mesmo pedido vale a comanda mais antiga (menor número).
/// <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).
/// </summary>
internal static class NumeroComandaDoPedido
{
    public static async Task<IReadOnlyDictionary<Guid, long>> PorPedidosAsync(
        EasyStockDbContext db, Guid empresaId, IReadOnlyCollection<Guid> pedidoIds, CancellationToken ct)
    {
        if (pedidoIds.Count == 0) return new Dictionary<Guid, long>();
        var ids = pedidoIds as List<Guid> ?? pedidoIds.ToList();

        var emAndamento = db.AtendimentoConversas.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.PedidoEmAndamentoId != null && ids.Contains(c.PedidoEmAndamentoId.Value))
            .Select(c => new { PedidoId = c.PedidoEmAndamentoId!.Value, c.NumeroComanda });
        var peloLink = db.LinksCardapioConversa.AsNoTracking()
            .Where(l => l.EmpresaId == empresaId && l.PedidoRegistradoId != null && ids.Contains(l.PedidoRegistradoId.Value))
            .Join(db.AtendimentoConversas.AsNoTracking().Where(c => c.EmpresaId == empresaId),
                l => l.ConversaId, c => c.Id,
                (l, c) => new { PedidoId = l.PedidoRegistradoId!.Value, c.NumeroComanda });

        return (await emAndamento.Concat(peloLink).ToListAsync(ct))
            .GroupBy(x => x.PedidoId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.NumeroComanda));
    }
}
