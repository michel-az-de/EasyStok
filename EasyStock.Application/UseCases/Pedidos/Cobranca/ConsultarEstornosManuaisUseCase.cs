using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

public sealed record PagamentoParaDevolucao(Guid Id, string Metodo, decimal Valor, DateTime PagoEm,
    decimal Devolvido, decimal Disponivel, bool Manual);
public sealed record EstornosManuaisResult(IReadOnlyList<PagamentoParaDevolucao> Pagamentos,
    IReadOnlyList<PedidoEstornoManual> Estornos);

public sealed class ConsultarEstornosManuaisUseCase(IPedidoRepository pedidos, ICobrancaPedidoRepository cobrancas)
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
        }).ToList(), estornos);
    }

    internal static bool EhManual(PedidoPagamento pagamento, IReadOnlyList<CobrancaPedido> cobrancas) =>
        !string.Equals(pagamento.RegistradoPorNome, "Mercado Pago", StringComparison.OrdinalIgnoreCase)
        && !cobrancas.Any(c => c.EhOnline && !string.IsNullOrEmpty(pagamento.Referencia)
            && c.PagamentoExternoId == pagamento.Referencia);
}
