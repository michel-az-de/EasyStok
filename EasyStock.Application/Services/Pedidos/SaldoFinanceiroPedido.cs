using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;

namespace EasyStock.Application.Services.Pedidos;

public sealed record SaldoFinanceiroComanda(decimal Recebido, decimal Devolvido, decimal ConvertidoEmCupom, decimal CupomAplicado, decimal Reservado)
{
    public decimal Alocado => Recebido - Devolvido - ConvertidoEmCupom + CupomAplicado;
    public decimal DinheiroDisponivel => Math.Max(0, Recebido - Devolvido - Math.Max(0, ConvertidoEmCupom - CupomAplicado) - Reservado);
}

public sealed class SaldoFinanceiroPedido(IPedidoRepository pedidos, IEstornoOnlineRepository estornos, ICreditoClienteComandaRepository creditos)
{
    public async Task<SaldoFinanceiroComanda> ConsultarAsync(Pedido pedido, CancellationToken ct)
    {
        var online = await estornos.ListarAsync(pedido.EmpresaId, pedido.Id, ct);
        var recebimentos = await estornos.RecebimentosAsync(pedido.EmpresaId, pedido.Id, ct);
        var legado = recebimentos.Where(r => r.Estornada && !online.Any(e => e.PagamentoId == r.PagamentoId)).Sum(r => r.Valor);
        var manual = await pedidos.ListarEstornosManuaisAsync(pedido.EmpresaId, pedido.Id, ct);
        var cupons = await creditos.TotaisDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        return new(pedido.TotalPago, legado + online.Where(e => e.Situacao == PedidoEstornoOnline.Confirmado).Sum(e => e.Valor) + manual.Sum(e => e.Valor),
            cupons.Emitido, cupons.Utilizado, online.Where(e => e.Situacao == PedidoEstornoOnline.Pendente).Sum(e => e.Valor));
    }
}
