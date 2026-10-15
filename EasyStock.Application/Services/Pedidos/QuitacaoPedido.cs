using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

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
    IOperacaoEventPublisher eventos,
    CalculadoraInicioPrevistoPedido inicioPrevisto,
    SaldoFinanceiroPedido? saldos = null)
{
    public const string Marco = "quitacao_confirmada";

    public async Task ExigirBaixaSemCobrancaOnlineAsync(Pedido pedido, CancellationToken ct)
    {
        var lista = await cobrancas.ListarDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        if (lista.Any(c => c.EmpresaId != pedido.EmpresaId || c.PedidoId != pedido.Id))
            throw new UseCaseValidationException("Cobrança não pertence ao pedido.");
        if (lista.Any(c => c.EhOnline && c.EstaPendente))
            throw new UseCaseValidationException("cobranca_online_pendente",
                "Há uma cobrança online pendente. Confira o pagamento antes de receber manualmente.");
    }

    public async Task<bool> PodeReceberSinalManualAsync(Pedido pedido, CancellationToken ct)
    {
        if (pedido.StatusEnum != StatusPedido.AguardandoPagamento
            || !PlanoPagamentoEncomenda.TemSinal(pedido.AgendadoParaEm, pedido.CriadoEm)) return false;
        var lista = (await cobrancas.ListarDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct))
            .Where(c => c.EmpresaId == pedido.EmpresaId && c.PedidoId == pedido.Id && c.EstaPendente).ToList();
        return lista.Any(c => !c.EhOnline) && !lista.Any(c => c.EhOnline);
    }

    public async Task<PedidoMudouStatusOperacao?> ConfirmarSinalManualAsync(Pedido pedido, PedidoPagamento pagamento, CancellationToken ct)
    {
        if (!await PodeReceberSinalManualAsync(pedido, ct)) return null;
        var alocado = saldos is not null ? (await saldos.ConsultarAsync(pedido, ct)).Alocado : pedido.TotalPago;
        if (alocado < PlanoPagamentoEncomenda.Sinal(pedido.Total.Valor)) return null;
        var anterior = pedido.Status;
        pedido.MudarStatus(pedido.RequerAprovacao ? StatusPedido.AguardandoAprovacaoBaba : StatusPedido.Aguardando);
        await inicioPrevisto.AplicarNaFilaAsync(pedido, ct);
        await publicador.PublicarAsync(pedido.EmpresaId, "pedido.mudou_status", "pedido", pedido.Id,
            new PedidoMudouStatusEvent(pedido.Id, pedido.EmpresaId, pedido.LojaId, anterior, pedido.Status,
                "manual", pagamento.RegistradoPorUserId, pagamento.RegistradoPorNome, pagamento.PagoEm),
            correlationId: pedido.Id.ToString(), ct: ct);
        return new(pedido.Id, anterior, pedido.Status);
    }

    public async Task<EfeitosQuitacao?> ConcluirManualAsync(Pedido pedido, PedidoPagamento pagamento, CancellationToken ct)
    {
        var saldo = saldos is null ? null : await saldos.ConsultarAsync(pedido, ct);
        if (pedido.Total.Valor <= 0 || (saldo?.Alocado ?? pedido.TotalPago) < pedido.Total.Valor || pedido.Origem == "balcao") return null;
        if (await pedidos.ExisteEventoAsync(pedido.EmpresaId, pedido.Id, Marco, ct)) return null;
        var lista = await cobrancas.ListarDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        var pagoOnline = lista.Where(c => c.EhOnline && c.Status == StatusCobrancaPedido.Paga)
            .Sum(c => c.ValorPago ?? 0m);
        var manual = lista.FirstOrDefault(c => c.EstaPendente && !c.EhOnline);
        if (manual is null)
        {
            var conversaId = lista.Select(c => c.ConversaId).FirstOrDefault(id => id is not null)
                ?? await conversas.ObterIdPorPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
            manual = CobrancaPedido.CriarNaEntrega(pedido.EmpresaId, pedido.Id,
                pedido.Total.Valor - pagoOnline - (saldo?.CupomAplicado ?? 0) + (saldo?.ConvertidoEmCupom ?? 0) + (saldo?.Devolvido ?? 0), pagamento.PagoEm, conversaId);
            await cobrancas.AddAsync(manual, ct);
        }
        manual.MarcarPaga($"manual-{pagamento.Id:N}", pedido.TotalPago - pagoOnline, pagamento.Metodo, pagamento.PagoEm);
        foreach (var outra in lista.Where(c => c.EstaPendente && c.Id != manual.Id))
            outra.Cancelar("quitacao_manual", pagamento.PagoEm);
        return await PrepararAsync(pedido, manual, pagamento.PagoEm, ct);
    }

    public async Task<EfeitosQuitacao?> PrepararAsync(Pedido pedido, CobrancaPedido cobranca, DateTime agora, CancellationToken ct)
    {
        var alocado = saldos is not null ? (await saldos.ConsultarAsync(pedido, ct)).Alocado : pedido.TotalPago;
        if (pedido.Total.Valor <= 0 || alocado < pedido.Total.Valor) return null;
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

    public async Task<EfeitosQuitacao?> PrepararCreditoAsync(Pedido pedido, Guid conversaId, DateTime agora, CancellationToken ct)
    {
        if (saldos is null || (await saldos.ConsultarAsync(pedido, ct)).Alocado < pedido.Total.Valor
            || await pedidos.ExisteEventoAsync(pedido.EmpresaId, pedido.Id, Marco, ct)) return null;
        await pedidos.AddEventoAsync(new PedidoEvento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = Marco,
            OcorridoEm = agora, Origem = "cupom_cliente", Detalhes = "Pedido quitado com crédito do cliente." });
        await publicador.PublicarAsync(pedido.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", pedido.Id,
            new PedidoPagoEvent(pedido.Id, pedido.EmpresaId, pedido.LojaId, pedido.ClienteId, conversaId, Guid.Empty,
                "credito_cliente", "cupom", "cupom", 0m, pedido.Status, agora), correlationId: pedido.Id.ToString(), ct: ct);
        return new(new PedidoPagoOperacao(pedido.Id, pedido.Id.ToString("N")[..8].ToUpperInvariant(),
            pedido.ClienteNome, pedido.Total.Valor, pedido.AgendadoParaEm), await PrepararCanhotoAsync(pedido, agora, ct));
    }

    public async Task ReabrirManualAsync(Pedido pedido, DateTime agora, CancellationToken ct)
    {
        if (pedido.TotalPago >= pedido.Total.Valor) return;
        var lista = await cobrancas.ListarDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        foreach (var c in lista.Where(c => !c.EhOnline && c.Status == StatusCobrancaPedido.Paga))
            c.ReabrirPagamentoManual(agora);
    }

    public async Task PublicarAsync(Guid empresaId, EfeitosQuitacao? efeito, CancellationToken ct, PedidoMudouStatusOperacao? mudanca = null)
    {
        if (mudanca is not null) await eventos.PublicarAsync(EventosOperacao.PedidoMudouStatus, empresaId, mudanca, ct);
        if (efeito?.Pago is { } pago) await eventos.PublicarAsync(EventosOperacao.PedidoPago, empresaId, pago, ct);
        if (efeito?.Impressao is { } impressao) await eventos.PublicarAsync(EventosOperacao.ImpressaoPendente, empresaId, impressao, ct);
    }
}
