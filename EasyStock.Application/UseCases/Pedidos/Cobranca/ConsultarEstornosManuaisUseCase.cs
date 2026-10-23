using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

public sealed record PagamentoParaDevolucao(Guid Id, string Metodo, decimal Valor, DateTime PagoEm,
    decimal Devolvido, decimal Disponivel, bool Manual);
public sealed record EstornosManuaisResult(IReadOnlyList<PagamentoParaDevolucao> Pagamentos,
    IReadOnlyList<PedidoEstornoManual> Estornos, decimal ADevolver = 0m);

public sealed class ConsultarEstornosManuaisUseCase(IPedidoRepository pedidos, ICobrancaPedidoRepository cobrancas,
    SaldoFinanceiroPedido? saldos = null)
{
    public async Task<EstornosManuaisResult> ExecuteAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var pedido = await pedidos.GetByIdWithDetailsAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId) throw new CobrancaPedidoNaoEncontradoException(pedidoId);
        var lista = await cobrancas.ListarDoPedidoAsync(empresaId, pedidoId, ct);
        var estornos = await pedidos.ListarEstornosManuaisAsync(empresaId, pedidoId, ct);
        return new(pedido.Pagamentos.Select(p =>
        {
            var devolvido = estornos.Where(e => e.PagamentoId == p.Id).Sum(e => e.Valor);
            var manual = EhManual(p, lista);
            return new PagamentoParaDevolucao(p.Id, p.Metodo, p.Valor, p.PagoEm, devolvido,
                manual ? Math.Max(0, p.Valor - devolvido) : 0, manual);
        }).ToList(), estornos, await ADevolverAsync(pedido, estornos, ct));
    }

    /// <summary>
    /// #1717-5: pedido cancelado com dinheiro recebido e ainda não devolvido (manual ou online), descontado o que virou
    /// cupom ou está com estorno pendente. Pedido ativo não tem devolução a fazer.
    /// </summary>
    private async Task<decimal> ADevolverAsync(Domain.Entities.Pedido pedido, IReadOnlyList<PedidoEstornoManual> estornos, CancellationToken ct)
    {
        if (pedido.StatusEnum != Domain.Sales.StatusPedido.Cancelado) return 0m;
        return saldos is not null
            ? (await saldos.ConsultarAsync(pedido, ct)).DinheiroDisponivel
            : Math.Max(0m, pedido.TotalPago - estornos.Sum(e => e.Valor));
    }

    internal static bool EhManual(PedidoPagamento pagamento, IReadOnlyList<CobrancaPedido> cobrancas) =>
        !string.Equals(pagamento.RegistradoPorNome, "Mercado Pago", StringComparison.OrdinalIgnoreCase)
        && !TemCobrancaOnline(pagamento, cobrancas);

    /// <summary>O pagamento confirmou uma cobrança online: só o estorno o devolve.</summary>
    internal static bool TemCobrancaOnline(PedidoPagamento pagamento, IReadOnlyList<CobrancaPedido> cobrancas) =>
        !string.IsNullOrEmpty(pagamento.Referencia)
        && cobrancas.Any(c => c.EhOnline && c.PagamentoExternoId == pagamento.Referencia);
}
