using EasyStock.Application.Services.Pedidos;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #2307: quanto falta receber do pedido, para o app nao oferecer cobranca de pedido ja quitado.
/// Mesma conta da quitacao (<see cref="SaldoFinanceiroPedido"/>): desconta estorno e cupom. Pedido sem
/// pagamento nao consulta nada; sem o servico (teste ou host antigo) cai na diferenca simples.
/// </summary>
public static class AReceberDoPedidoMobile
{
    public static async Task<decimal> CalcularAsync(Pedido pedido, SaldoFinanceiroPedido? saldos, CancellationToken ct)
    {
        var total = pedido.Total.Valor;
        if (total <= 0m) return 0m;
        if (pedido.TotalPago <= 0m) return total;
        return saldos is null ? Math.Max(0m, total - pedido.TotalPago) : await saldos.AReceberAsync(pedido, ct);
    }
}
