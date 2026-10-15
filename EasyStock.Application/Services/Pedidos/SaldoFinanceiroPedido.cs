using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;

namespace EasyStock.Application.Services.Pedidos;

public sealed record SaldoFinanceiroComanda(decimal Recebido, decimal Devolvido, decimal ConvertidoEmCupom, decimal CupomAplicado, decimal Reservado)
{
    public decimal Alocado => Recebido - Devolvido - ConvertidoEmCupom + CupomAplicado;
    public decimal AReceber(decimal total) => total <= 0m ? 0m : Math.Max(0m, total - Alocado);
    public decimal DinheiroDisponivel => Math.Max(0, Recebido - Devolvido - Math.Max(0, ConvertidoEmCupom - CupomAplicado) - Reservado);
}

public sealed class SaldoFinanceiroPedido(IPedidoRepository pedidos, IEstornoOnlineRepository estornos, ICreditoClienteComandaRepository creditos)
{
    public Task<SaldoFinanceiroComanda> ConsultarAsync(Pedido pedido, CancellationToken ct) =>
        ConsultarAsync(pedido.EmpresaId, pedido.Id, pedido.TotalPago, ct);

    /// <summary>Mesma conta sem o agregado carregado (leitura do KDS, #1776): basta a soma dos pagamentos.</summary>
    public async Task<SaldoFinanceiroComanda> ConsultarAsync(Guid empresaId, Guid pedidoId, decimal totalPago, CancellationToken ct)
    {
        var online = await estornos.ListarAsync(empresaId, pedidoId, ct);
        var recebimentos = await estornos.RecebimentosAsync(empresaId, pedidoId, ct);
        var legado = recebimentos.Where(r => r.Estornada && !online.Any(e => e.PagamentoId == r.PagamentoId)).Sum(r => r.Valor);
        var manual = await pedidos.ListarEstornosManuaisAsync(empresaId, pedidoId, ct);
        var cupons = await creditos.TotaisDoPedidoAsync(empresaId, pedidoId, ct);
        return new(totalPago, legado + online.Where(e => e.Situacao == PedidoEstornoOnline.Confirmado).Sum(e => e.Valor) + manual.Sum(e => e.Valor),
            cupons.Emitido, cupons.Utilizado, online.Where(e => e.Situacao == PedidoEstornoOnline.Pendente).Sum(e => e.Valor));
    }

    /// <summary>
    /// #1776 (item 19): quanto falta receber do pedido, pela mesma conta da quitação (total menos o alocado).
    /// "Tem pagamento" não é "pago": o sinal de R$ 25 num pedido de R$ 50 deixa R$ 25 para a porta.
    /// </summary>
    public Task<decimal> AReceberAsync(Pedido pedido, CancellationToken ct) =>
        AReceberAsync(pedido.EmpresaId, pedido.Id, pedido.Total.Valor, pedido.TotalPago, ct);

    public async Task<decimal> AReceberAsync(Guid empresaId, Guid pedidoId, decimal total, decimal totalPago, CancellationToken ct) =>
        total <= 0m ? 0m : (await ConsultarAsync(empresaId, pedidoId, totalPago, ct)).AReceber(total);

    /// <summary>
    /// #1776 (item 19): marcar entregue com saldo a receber só com a confirmação de quem marca (recebeu na
    /// porta ou vai cobrar depois). A tela avisa; a trava é esta.
    /// </summary>
    public async Task ExigirQuitadoParaEntregarAsync(Pedido pedido, bool saldoConfirmado, CancellationToken ct)
    {
        if (saldoConfirmado) return;
        var falta = await AReceberAsync(pedido, ct);
        if (falta > 0) throw new SaldoAReceberNaEntregaException(falta);
    }
}

/// <summary>#1776 (item 19): entrega com saldo a receber sem confirmação. A API responde 409 com o código.</summary>
public sealed class SaldoAReceberNaEntregaException(decimal valor) : Exception(
    $"Falta receber {valor.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")).Replace('\u00a0', ' ')} deste pedido. "
    + "Confirme que recebeu na porta ou que vai cobrar depois.")
{
    public const string Codigo = "SALDO_A_RECEBER";
    public decimal Valor { get; } = valor;
}
