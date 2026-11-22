using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;

namespace EasyStock.Application.UseCases.ObterPedidoDetalhes;

public sealed record ObterPedidoDetalhesQuery(Guid EmpresaId, Guid Id, int MaxEventos = 200);

public class ObterPedidoDetalhesUseCase(IPedidoRepository repo, EasyStock.Application.Services.Pedidos.SaldoFinanceiroPedido? saldos = null)
{
    public async Task<PedidoDetalheResult?> ExecuteAsync(ObterPedidoDetalhesQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(q.Id, "Id");

        var p = await repo.GetByIdWithDetailsAsync(q.EmpresaId, q.Id);
        if (p == null) return null;

        var eventos = await repo.GetEventosAsync(p.Id, q.MaxEventos);

        // E4 L1 (#3076): o que falta receber pela conta da baixa (cupom e estorno; zero no quitado do histórico).
        var pedido = CriarPedidoUseCase.Map(p);
        if (saldos is not null)
            pedido = pedido with { AReceber = await saldos.AReceberAsync(p, ct) };

        return new PedidoDetalheResult(
            pedido,
            p.Itens.OrderBy(i => i.CriadoEm).Select(i => new PedidoItemResult(
                i.Id, i.PedidoId, i.ProdutoId, i.Nome, i.Emoji, i.Unidade,
                i.Quantidade, i.PrecoUnitario, i.Subtotal,
                i.Observacao, i.CriadoEm)).ToList(),
            eventos.Select(e => new PedidoEventoResult(
                e.Id, e.PedidoId, e.Tipo,
                e.StatusAntigo, e.StatusNovo,
                e.Detalhes, e.UsuarioId, e.UsuarioNome,
                e.Origem, e.OcorridoEm)).ToList(),
            p.Pagamentos.OrderByDescending(pg => pg.PagoEm).Select(pg => new PedidoPagamentoResult(
                pg.Id, pg.PedidoId, pg.Metodo, pg.Valor,
                pg.Referencia, pg.Observacao,
                pg.PagoEm, pg.RegistradoPorUserId, pg.RegistradoPorNome)).ToList()
        );
    }
}
