using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.Services.Pedidos;

public sealed record EfeitosQuitacao(PedidoPagoOperacao? Pago, ImpressaoPendenteOperacao? Impressao);

// O marco permanece na trilha mesmo quando a baixa é desfeita: uma correção não manda
// outro "recebemos seu pagamento" nem imprime outra via automaticamente (#1255).
public sealed class QuitacaoPedido(
    IPedidoRepository pedidos,
    ICobrancaPedidoRepository cobrancas,
    IConversaRepository conversas,
    IImpressaoPendenteRepository impressoes,
    IPublicadorEventoIntegracao publicador,
    IOperacaoEventPublisher eventos)
{
    public const string Marco = "quitacao_confirmada";

    public async Task<EfeitosQuitacao?> ConcluirManualAsync(Pedido pedido, PedidoPagamento pagamento, CancellationToken ct)
    {
        if (pedido.Total.Valor <= 0 || pedido.TotalPago < pedido.Total.Valor || pedido.Origem == "balcao") return null;
        var lista = await cobrancas.ListarDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        if (lista.Any(c => c.Status == StatusCobrancaPedido.Paga)) return null;
        var manual = lista.FirstOrDefault(c => c.EstaPendente && !c.EhOnline);
        if (manual is null)
        {
            var conversaId = lista.Select(c => c.ConversaId).FirstOrDefault(id => id is not null)
                ?? await conversas.ObterIdPorPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
            manual = CobrancaPedido.CriarNaEntrega(pedido.EmpresaId, pedido.Id, pedido.Total.Valor, pagamento.PagoEm, conversaId);
            await cobrancas.AddAsync(manual, ct);
        }
        manual.MarcarPaga($"manual-{pagamento.Id:N}", pedido.TotalPago, pagamento.Metodo, pagamento.PagoEm);
        foreach (var outra in lista.Where(c => c.EstaPendente && c.Id != manual.Id))
            outra.Cancelar("quitacao_manual", pagamento.PagoEm);
        return await PrepararAsync(pedido, manual, pagamento.PagoEm, ct);
    }

    public async Task<EfeitosQuitacao?> PrepararAsync(Pedido pedido, CobrancaPedido cobranca, DateTime agora, CancellationToken ct)
    {
        if (await pedidos.ExisteEventoAsync(pedido.EmpresaId, pedido.Id, Marco, ct)) return null;
        await pedidos.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = Marco, OcorridoEm = agora,
            Origem = cobranca.EhOnline ? "mercadopago" : "manual", Detalhes = "Pedido quitado. Confirmação emitida uma vez."
        });
        var conversaId = cobranca.ConversaId ?? await conversas.ObterIdPorPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        await publicador.PublicarAsync(pedido.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", pedido.Id,
            new PedidoPagoEvent(pedido.Id, pedido.EmpresaId, pedido.LojaId, pedido.ClienteId, conversaId, cobranca.Id,
                cobranca.Provedor, cobranca.PagamentoExternoId!, cobranca.MetodoPagamento!, cobranca.ValorPago!.Value,
                pedido.Status, agora), correlationId: pedido.Id.ToString(), ct: ct);
        return new(new PedidoPagoOperacao(pedido.Id, pedido.Id.ToString("N")[..8].ToUpperInvariant(),
            pedido.ClienteNome, pedido.Total.Valor, pedido.AgendadoParaEm), await PrepararCanhotoAsync(pedido, agora, ct));
    }

    public async Task<ImpressaoPendenteOperacao?> PrepararCanhotoAsync(Pedido pedido, DateTime agora, CancellationToken ct)
    {
        if (await impressoes.ExisteCanhotoAsync(pedido.EmpresaId, pedido.Id, ct)) return null;
        var canhoto = ImpressaoPendente.CriarCanhoto(pedido.EmpresaId, pedido.LojaId, pedido.Id, agora);
        await impressoes.AddAsync(canhoto, ct);
        return new(canhoto.Id, pedido.Id);
    }

    public async Task ReabrirManualAsync(Pedido pedido, DateTime agora, CancellationToken ct)
    {
        if (pedido.TotalPago >= pedido.Total.Valor) return;
        var lista = await cobrancas.ListarDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        foreach (var c in lista.Where(c => !c.EhOnline && c.Status == StatusCobrancaPedido.Paga))
            c.ReabrirPagamentoManual(agora);
    }

    public async Task PublicarAsync(Guid empresaId, EfeitosQuitacao? efeito, CancellationToken ct)
    {
        if (efeito?.Pago is { } pago) await eventos.PublicarAsync(EventosOperacao.PedidoPago, empresaId, pago, ct);
        if (efeito?.Impressao is { } impressao) await eventos.PublicarAsync(EventosOperacao.ImpressaoPendente, empresaId, impressao, ct);
    }
}
