using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.ListarPedidosCliente;

public sealed record ListarPedidosQuery(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? ClienteId = null,
    DateTime? Desde = null,
    DateTime? Ate = null,
    string? Search = null,
    string? Sort = "criadoem",
    string? Order = "desc",
    // E4 L2 (#3076): dia da entrega = AgendadoParaEm, ou CriadoEm no pedido para agora.
    DateTime? EntregaDesde = null,
    DateTime? EntregaAte = null,
    // #3294: só a tela Pedidos de Cadastros pede os sinais da linha; Web e Cozinha seguem com a linha de sempre.
    bool ComSinais = false);

public sealed record ListarPedidosResult(
    IReadOnlyList<PedidoResult> Items,
    int Total,
    int Page,
    int PageSize);

public class ListarPedidosUseCase(IPedidoRepository repo, IPedidosResumoQuery resumo, TimeProvider relogio)
{
    /// <summary>Itens que a linha da lista mostra; o resto a tela conta por <c>ItensCount</c>.</summary>
    public const int ItensNaLinha = 3;

    public async Task<ListarPedidosResult> ExecuteAsync(ListarPedidosQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (items, total) = await repo.GetByEmpresaAsync(
            q.EmpresaId, page, size,
            q.Status, q.ClienteId, q.Desde, q.Ate,
            q.Search, q.Sort, q.Order, q.EntregaDesde, q.EntregaAte);
        var pedidos = items.ToList();
        if (!q.ComSinais)
            return new ListarPedidosResult(pedidos.Select(CriarPedidoUseCase.Map).ToList(), total, page, size);

        // #3294: uma leitura por página para a ordem da compra e a situação; a lista nunca faz N+1.
        var sinais = pedidos.Count == 0
            ? new Dictionary<Guid, SinaisDoPedido>()
            : await resumo.SinaisAsync(q.EmpresaId, pedidos.Select(p => p.Id).ToList(), ct);

        var agora = relogio.GetUtcNow().UtcDateTime;
        return new ListarPedidosResult(
            pedidos.Select(p => NaLista(p, sinais.GetValueOrDefault(p.Id), agora)).ToList(),
            total, page, size);
    }

    private static PedidoResult NaLista(EasyStock.Domain.Entities.Pedido p, SinaisDoPedido? sinais, DateTime agoraUtc) =>
        CriarPedidoUseCase.Map(p) with
        {
            // A ordem em que os itens entraram no pedido; o Include não garante ordem.
            // A linha de frete não é item pedido (#2018): fica fora do resumo e da conta do "+N".
            ItensResumo = p.Itens.Where(i => !i.EhLinhaDeFrete).OrderBy(i => i.CriadoEm).ThenBy(i => i.Id).Take(ItensNaLinha)
                .Select(i => new PedidoItemNaLista(i.Nome, i.Quantidade, i.Unidade)).ToList(),
            ItensSemFrete = p.Itens.Count(i => !i.EhLinhaDeFrete),
            CompraDoCliente = sinais?.CompraDoCliente,
            SituacaoDesde = sinais?.SituacaoDesde ?? p.CriadoEm,
            Atrasado = RegraDeAtraso.EstaAtrasado(p.Status, p.AgendadoParaEm, agoraUtc),
        };
}
