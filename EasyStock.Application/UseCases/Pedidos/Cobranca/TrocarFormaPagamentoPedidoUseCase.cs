using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <param name="Forma"><c>online</c> (link do Mercado Pago) ou <c>na_entrega</c> (maquininha ou dinheiro).</param>
public sealed record TrocarFormaPagamentoPedidoInput(
    Guid EmpresaId, Guid PedidoId, string Forma, Guid? UsuarioId = null, string? UsuarioNome = null);

public sealed record TrocarFormaPagamentoPedidoResult(CobrancaPedidoResult Cobranca, bool EnviadaNaConversa);

/// <summary>
/// Troca a forma de pagamento do pedido sem esperar o link expirar (S11, feedback da operadora de
/// 26/09/2026). Lock no pedido; pedido pago → 409 <c>pedido_ja_pago</c>; a cobrança pendente vira
/// <c>Cancelada</c> e nasce a nova na forma pedida; a troca vai para a trilha com o usuário.
///
/// <list type="bullet">
///   <item><c>online</c>: preferência nova com a mesma tentativa da anterior (a troca não consome a
///     reemissão do job); com conversa aberta, o link sai na conversa. O status do pedido não muda.</item>
///   <item><c>na_entrega</c>: cobrança online pendente é conferida e impede nova cobrança manual; sem link,
///     pedido em <c>AguardandoPagamento</c> vai para <c>Aguardando</c>
///     (entra na fila e paga na entrega), senão o job o cancelaria por falta de pagamento.</item>
/// </list>
///
/// <para>
/// S32: depois do commit, a preferência online substituída é expirada no Mercado Pago
/// (<c>PUT checkout/preferences/{id}</c>), best-effort: falha só loga. Um pagamento que ainda chegue pelo
/// link antigo é confirmado mesmo assim (<see cref="ConfirmarPagamentoPedidoUseCase"/>: dinheiro recebido vence).
/// </para>
/// </summary>
public sealed class TrocarFormaPagamentoPedidoUseCase(
    IPedidoStorefrontRepository pedidoRepository,
    ICobrancaPedidoRepository cobrancaRepository,
    GerarCobrancaPedidoUseCase gerarCobranca,
    AvisoCobrancaConversa aviso,
    IPublicadorEventoIntegracao publicador,
    IMercadoPagoClient mercadoPagoClient,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<TrocarFormaPagamentoPedidoUseCase> logger,
    CalculadoraInicioPrevistoPedido inicioPrevisto,
    IOperacaoEventPublisher operacaoEventos,
    QuitacaoPedido quitacao,
    IConferenciaPagamentoPedido conferencia,
    SaldoFinanceiroPedido? saldos = null)
{
    public const string FormaOnline = "online";
    public const string FormaNaEntrega = "na_entrega";

    public async Task<TrocarFormaPagamentoPedidoResult> ExecuteAsync(TrocarFormaPagamentoPedidoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");
        var forma = (input.Forma ?? string.Empty).Trim().ToLowerInvariant();
        if (forma is not (FormaOnline or FormaNaEntrega))
            throw new UseCaseValidationException("Forma de pagamento deve ser 'online' ou 'na_entrega'.");

        Guid? linkConferido = null;
        if (forma == FormaNaEntrega)
        {
            var anteriores = await cobrancaRepository.ListarDoPedidoAsync(input.EmpresaId, input.PedidoId, ct);
            var linkPendente = anteriores.FirstOrDefault(c => c.EmpresaId == input.EmpresaId && c.PedidoId == input.PedidoId && c.EhOnline && c.EstaPendente);
            if (linkPendente is not null)
            {
                // #1733: o link aberto é conferido no provedor antes de ser cancelado. Pago, o pedido já está
                // quitado (409 e a tela atualiza). Sem pagamento no provedor, a troca segue e cancela o link
                // (#1726: liberar sem pagamento conserva a dívida na entrega). Qualquer outra situação
                // (em processamento, aprovado sem conciliar, credencial que não alcança) recusa a baixa manual.
                var resultado = await conferencia.ExecuteAsync(input.EmpresaId, input.PedidoId, ct);
                if (resultado.Confirmado)
                    throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoJaPago,
                        "Pagamento online conferido. Atualize o pedido antes de receber outro valor.");
                if (resultado.Situacao != ConferirPagamentoPedidoUseCase.SituacaoNaoEncontrado)
                    throw new UseCaseValidationException(MotivoLinkPendente(resultado.Situacao));
                linkConferido = linkPendente.Id;
            }
        }

        var (cobranca, conversaId, preferenciaSubstituida, mudanca, impressao) = await unitOfWork.ExecuteInTransactionSemRetryAsync(
            async token => await TrocarNoLockAsync(input, forma, linkConferido, token), ct);

        // A cozinha só relê depois que a transação terminou; na entrega ainda não é pagamento.
        if (mudanca is not null)
            await operacaoEventos.PublicarAsync(EventosOperacao.PedidoMudouStatus, input.EmpresaId, mudanca, ct);

        if (impressao is not null)
            await operacaoEventos.PublicarAsync(EventosOperacao.ImpressaoPendente, input.EmpresaId, impressao, ct);

        if (preferenciaSubstituida is not null)
            await ExpirarPreferenciaAsync(preferenciaSubstituida, ct);

        var enviada = false;
        if (conversaId is { } conversa && cobranca.LinkPagamento is { } link)
            enviada = await aviso.EnviarAsync(input.EmpresaId, conversa, AvisoCobrancaConversa.TextoLinkTrocado(link),
                relogio.GetUtcNow().UtcDateTime, ct);

        return new TrocarFormaPagamentoPedidoResult(cobranca, enviada);
    }

    public const string MensagemLinkPendente = "Há uma cobrança online pendente. Confira o pagamento antes de receber manualmente.";

    private static string MotivoLinkPendente(string situacao) => situacao switch
    {
        ConferirPagamentoPedidoUseCase.SituacaoEmProcessamento =>
            "Há uma cobrança online pendente e o provedor ainda está processando o pagamento. Confira novamente em instantes.",
        ConferirPagamentoPedidoUseCase.SituacaoNaoConciliado =>
            "Há uma cobrança online pendente com pagamento aprovado que não pôde ser conciliado. Confira a conta do Mercado Pago.",
        ConferirPagamentoPedidoUseCase.SituacaoAguardandoCredencial =>
            "Há uma cobrança online pendente e o Mercado Pago não pôde ser consultado. Conecte a credencial antes de receber manualmente.",
        _ => MensagemLinkPendente,
    };

    private Task ExpirarPreferenciaAsync(string preferenceId, CancellationToken ct) =>
        ExpiracaoPreferenciaMercadoPago.ExpirarAsync(() => mercadoPagoClient, preferenceId, relogio.GetUtcNow().UtcDateTime, logger, ct);

    /// <param name="linkConferido">
    /// #1717-2: link online pendente que o provedor confirmou sem pagamento antes da trava. Só ele pode ser cancelado
    /// pela troca para a entrega; outro link pendente nascido no meio não foi consultado e continua recusando.
    /// </param>
    private async Task<(CobrancaPedidoResult, Guid?, string?, PedidoMudouStatusOperacao?, ImpressaoPendenteOperacao?)> TrocarNoLockAsync(
        TrocarFormaPagamentoPedidoInput input, string forma, Guid? linkConferido, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var pedido = await pedidoRepository.GetForUpdateAsync(input.PedidoId, ct);
        if (pedido is null || pedido.EmpresaId != input.EmpresaId)
            throw new CobrancaPedidoNaoEncontradoException(input.PedidoId);
        if (PedidoStateMachine.EstaFinalizado(pedido.StatusEnum))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoFinalizado,
                "Pedido cancelado ou entregue não troca a forma de pagamento.");
        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(input.EmpresaId, pedido.Id, ct);
        // #1724-6: sinal pago online não é "pedido já pago"; a guarda vale só sem saldo a receber. O que as
        // cobranças pagas registram também conta: a confirmação do provedor pode ter gravado só a cobrança.
        var alocado = Math.Max(
            saldos is not null ? (await saldos.ConsultarAsync(pedido, ct)).Alocado : pedido.TotalPago,
            cobrancas.Where(c => c.Status == Domain.Enums.Pagamentos.StatusCobrancaPedido.Paga).Sum(c => c.ValorPago ?? 0m));
        var restante = pedido.Total.Valor - alocado;
        if (pedido.Total.Valor > 0m && restante <= 0m)
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoJaPago, "Pedido já pago.");
        // #1717-2 (decisão do Felipe em 13/10): o sinal da encomenda pode ser baixado à mão, como o pedido comum. O
        // link conferido sem pagamento é cancelado e a baixa manual segue; link pendente que não foi conferido recusa.
        if (forma == FormaNaEntrega && cobrancas.Any(c => c.EstaPendente && c.EhOnline && c.Id != linkConferido))
            throw new UseCaseValidationException(MensagemLinkPendente);

        var pendente = cobrancas.FirstOrDefault(c => c.EstaPendente);
        var provedorPedido = forma == FormaOnline ? CobrancaPedido.ProvedorMercadoPago : CobrancaPedido.ProvedorNaEntrega;
        if (pendente is not null && pendente.Provedor == provedorPedido && !pendente.Venceu(agora))
            return (CobrancaPedidoResult.De(pendente, reutilizada: true), pendente.ConversaId, null, null, null);

        var conversaId = cobrancas.Where(c => c.ConversaId is not null).Select(c => c.ConversaId).LastOrDefault();
        var formaAnterior = pendente?.Provedor ?? "nenhuma";
        pendente?.Cancelar($"troca_forma: {forma}", agora);
        var preferenciaSubstituida = pendente is { EhOnline: true } ? pendente.ReferenciaExterna : null;

        PedidoMudouStatusOperacao? mudanca = null;
        ImpressaoPendenteOperacao? impressao = null;
        CobrancaPedidoResult nova;
        if (forma == FormaOnline)
        {
            var tentativa = cobrancas.Where(c => c.EhOnline).Select(c => c.Tentativa).DefaultIfEmpty(1).Max();
            nova = await gerarCobranca.ExecuteAsync(
                new GerarCobrancaPedidoInput(input.EmpresaId, pedido.Id, conversaId, tentativa), ct);
            preferenciaSubstituida ??= nova.PreferenciaSubstituida;
        }
        else
        {
            // Com algo já recebido (sinal, cupom), a cobrança na entrega é só do restante.
            var naEntrega = CobrancaPedido.CriarNaEntrega(input.EmpresaId, pedido.Id,
                alocado > 0m ? restante : pedido.Total.Valor, agora, conversaId);
            await cobrancaRepository.AddAsync(naEntrega, ct);
            mudanca = await ColocarNaFilaAsync(pedido, input, agora, ct);
            if (mudanca is not null) impressao = await quitacao.PrepararCanhotoAsync(pedido, agora, ct);
            nova = CobrancaPedidoResult.De(naEntrega);
        }

        await pedidoRepository.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "forma_pagamento_trocada",
            Detalhes = $"{formaAnterior} -> {provedorPedido}",
            UsuarioId = input.UsuarioId,
            UsuarioNome = input.UsuarioNome,
            Origem = "web",
            OcorridoEm = agora,
        }, ct);
        await unitOfWork.CommitAsync();

        return (nova, conversaId, preferenciaSubstituida, mudanca, impressao);
    }

    private async Task<PedidoMudouStatusOperacao?> ColocarNaFilaAsync(PedidoEntity pedido, TrocarFormaPagamentoPedidoInput input, DateTime agora, CancellationToken ct)
    {
        if (pedido.StatusEnum != StatusPedido.AguardandoPagamento) return null;
        if (PlanoPagamentoEncomenda.TemSinal(pedido.AgendadoParaEm, pedido.CriadoEm)
            && pedido.TotalPago < PlanoPagamentoEncomenda.Sinal(pedido.Total.Valor)) return null;

        var statusAntigo = pedido.Status;
        // #1291: pedido de exceção (ex.: lead liberado fora de área) espera a dona, como no pago online.
        pedido.MudarStatus(pedido.RequerAprovacao ? StatusPedido.AguardandoAprovacaoBaba : StatusPedido.Aguardando);
        // #1230: paga na entrega, mas a janela já é compromisso: início previsto como no Mercado Pago.
        await inicioPrevisto.AplicarNaFilaAsync(pedido, ct);
        await pedidoRepository.UpdateAsync(pedido, ct);
        await publicador.PublicarAsync(
            input.EmpresaId, "pedido.mudou_status", "pedido", pedido.Id,
            new PedidoMudouStatusEvent(pedido.Id, input.EmpresaId, pedido.LojaId, statusAntigo, pedido.Status,
                "web", input.UsuarioId, input.UsuarioNome, agora),
            correlationId: pedido.Id.ToString(), ct: ct);
        return new PedidoMudouStatusOperacao(pedido.Id, statusAntigo, pedido.Status);
    }
}
