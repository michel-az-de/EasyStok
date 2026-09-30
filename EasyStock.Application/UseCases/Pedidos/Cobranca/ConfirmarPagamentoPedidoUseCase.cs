using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>
/// Pagamento consultado na fonte (S32: <c>GET v1/payments/{id}</c>) e entregue para confirmação.
/// </summary>
/// <param name="PedidoId"><c>external_reference</c> do pagamento.</param>
/// <param name="PagamentoExternoId"><c>id</c> do pagamento no Mercado Pago (vira <c>PedidoPagamento.Referencia</c>).</param>
/// <param name="StatusPagamento"><c>status</c> do pagamento: só <c>approved</c> confirma.</param>
/// <param name="ValorPago"><c>transaction_amount</c>.</param>
/// <param name="MetodoPagamentoExterno"><c>payment_method_id</c> (ex.: <c>pix</c>, <c>visa</c>).</param>
/// <param name="TipoPagamentoExterno"><c>payment_type_id</c> (ex.: <c>credit_card</c>, <c>debit_card</c>).</param>
/// <param name="PagoEm"><c>date_approved</c>; sem ele, o instante da confirmação.</param>
/// <param name="ReferenciaExterna">Id da preferência paga, quando o processor souber; sem ele, vale a
/// cobrança online pendente do pedido ou, não havendo, a mais recente.</param>
/// <param name="Provedor">Provedor da cobrança (<c>mercadopago</c>).</param>
public sealed record ConfirmarPagamentoPedidoInput(
    Guid PedidoId,
    string PagamentoExternoId,
    string StatusPagamento,
    decimal ValorPago,
    string? MetodoPagamentoExterno = null,
    string? TipoPagamentoExterno = null,
    DateTime? PagoEm = null,
    string? ReferenciaExterna = null,
    string Provedor = CobrancaPedido.ProvedorMercadoPago);

/// <summary>Desfecho da confirmação. Nenhum deles lança: o processor responde 200 ao Mercado Pago.</summary>
public enum SituacaoConfirmacaoPagamento
{
    Confirmado = 1,
    JaConfirmado = 2,
    NaoAprovado = 3,
    ValorMenor = 4,
    PedidoCancelado = 5,
    SemCobranca = 6,
    PedidoNaoEncontrado = 7,
}

public sealed record ConfirmarPagamentoPedidoResult(
    SituacaoConfirmacaoPagamento Situacao,
    Guid? CobrancaId = null,
    string? StatusPedido = null)
{
    public bool Confirmado => Situacao == SituacaoConfirmacaoPagamento.Confirmado;
}

/// <summary>
/// Confirma o pagamento online de um pedido (S11). Ponto de entrada do processor do webhook do
/// Mercado Pago (S32) e do ramo de conciliação do <c>CobrancaPedidoJob</c>.
///
/// <para>Fluxo, numa transação só:</para>
/// <list type="number">
///   <item>Descobre o tenant pelas cobranças do pedido (o webhook chega sem JWT) e liga o contexto.</item>
///   <item><c>SELECT FOR UPDATE</c> no pedido (padrão de <c>AprovarPedidoStorefrontUseCase</c>).</item>
///   <item>Mesmo <see cref="ConfirmarPagamentoPedidoInput.PagamentoExternoId"/> já pago → no-op.</item>
///   <item>Valor menor que o da cobrança → não confirma, grava o motivo na cobrança e na trilha.</item>
///   <item><c>AguardandoPagamento → Aguardando</c>, ou <c>AguardandoAprovacaoBaba</c> quando
///     <c>Pedido.RequerAprovacao</c> (S12); pedido já na fila só recebe o pagamento. Cobrança
///     <c>Paga</c>, demais pendentes canceladas, <c>PedidoPagoEvent</c> e <c>pedido.mudou_status</c> no
///     outbox e <c>PedidoPagamento</c> pelo <see cref="RegistrarPagamentoPedidoUseCase"/>.</item>
///   <item><c>InicioPrevistoEm</c> pela <see cref="CalculadoraInicioPrevistoPedido"/> (S21).</item>
/// </list>
///
/// <para>
/// Cobrança já <c>Cancelada</c> ou <c>Expirada</c> ainda é confirmada: dinheiro recebido vence. Pedido
/// cancelado não volta à fila; o motivo fica na cobrança para o estorno (S27).
/// </para>
///
/// <para>A impressão (S20) consome o <see cref="PedidoPagoEvent"/>.</para>
///
/// <para>
/// Depois que a transação fecha, publica <c>pedido.pago</c> no SSE de operação (S18) para o console tocar o
/// som. Commit que falha não publica nada.
/// </para>
/// </summary>
public sealed class ConfirmarPagamentoPedidoUseCase(
    ICobrancaPedidoRepository cobrancaRepository,
    IPedidoStorefrontRepository pedidoRepository,
    RegistrarPagamentoPedidoUseCase registrarPagamento,
    IPublicadorEventoIntegracao publicador,
    IOperacaoEventPublisher operacaoEventos,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<ConfirmarPagamentoPedidoUseCase> logger,
    CalculadoraInicioPrevistoPedido inicioPrevisto)
{
    public const string StatusAprovado = "approved";
    private const string Origem = "mercadopago";

    public async Task<ConfirmarPagamentoPedidoResult> ExecuteAsync(ConfirmarPagamentoPedidoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");
        if (string.IsNullOrWhiteSpace(input.PagamentoExternoId))
            throw new UseCaseValidationException("PagamentoExternoId é obrigatório.");

        if (!string.Equals(input.StatusPagamento, StatusAprovado, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Pagamento nao aprovado pedidoId={PedidoId} pagamento={Pagamento} status={Status}",
                input.PedidoId, input.PagamentoExternoId, input.StatusPagamento);
            return new(SituacaoConfirmacaoPagamento.NaoAprovado);
        }

        var empresaId = await cobrancaRepository.ObterEmpresaIdDoPedidoAsync(input.PedidoId, ct);
        if (empresaId is null)
        {
            logger.LogWarning("Pagamento sem cobranca pedidoId={PedidoId} pagamento={Pagamento}",
                input.PedidoId, input.PagamentoExternoId);
            return new(SituacaoConfirmacaoPagamento.SemCobranca);
        }
        tenantContext.SetCurrentTenant(empresaId.Value);

        var (resultado, pago) = await unitOfWork.ExecuteInTransactionSemRetryAsync(
            token => ConfirmarNoLockAsync(input, empresaId.Value, token), ct);

        // Evento de UI: só depois do commit da transação, nunca para uma confirmação desfeita.
        if (pago is not null)
            await operacaoEventos.PublicarAsync(EventosOperacao.PedidoPago, empresaId.Value, pago, ct);
        return resultado;
    }

    private async Task<(ConfirmarPagamentoPedidoResult, PedidoPagoOperacao?)> ConfirmarNoLockAsync(
        ConfirmarPagamentoPedidoInput input, Guid empresaId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var pedido = await pedidoRepository.GetForUpdateAsync(input.PedidoId, ct);
        if (pedido is null || pedido.EmpresaId != empresaId)
            return (new(SituacaoConfirmacaoPagamento.PedidoNaoEncontrado), null);

        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(empresaId, pedido.Id, ct);
        var jaPaga = cobrancas.FirstOrDefault(c => c.PagamentoExternoId == input.PagamentoExternoId);
        if (jaPaga is not null)
            return (new(SituacaoConfirmacaoPagamento.JaConfirmado, jaPaga.Id, pedido.Status), null);

        var alvo = EscolherCobranca(cobrancas, input);
        if (alvo is null)
            return (new(SituacaoConfirmacaoPagamento.SemCobranca, null, pedido.Status), null);

        if (input.ValorPago < alvo.Valor)
        {
            var motivo = $"valor_menor: pagamento {input.PagamentoExternoId} de {input.ValorPago.ToString("F2", Cultura.PtBr)} " +
                         $"para cobrança de {alvo.Valor.ToString("F2", Cultura.PtBr)}";
            return (await RecusarAsync(pedido, alvo, motivo, SituacaoConfirmacaoPagamento.ValorMenor, agora, ct), null);
        }

        if (pedido.StatusEnum == StatusPedido.Cancelado)
        {
            var motivo = $"pedido_cancelado: pagamento {input.PagamentoExternoId} recebido depois do cancelamento; estornar (S27)";
            return (await RecusarAsync(pedido, alvo, motivo, SituacaoConfirmacaoPagamento.PedidoCancelado, agora, ct), null);
        }

        var pagoEm = input.PagoEm ?? agora;
        var metodo = CobrancaPedido.MapearMetodo(input.MetodoPagamentoExterno, input.TipoPagamentoExterno);
        alvo.MarcarPaga(input.PagamentoExternoId, input.ValorPago, metodo, pagoEm);
        foreach (var outra in cobrancas.Where(c => c.EstaPendente && c.Id != alvo.Id))
            outra.Cancelar($"pago_por_outra_cobranca: {alvo.Id}", agora);

        // S21: com o pagamento, a janela vira compromisso; o card do KDS atrasa a partir daqui.
        pedido.DefinirInicioPrevisto(await inicioPrevisto.CalcularAsync(pedido, ct));

        var statusAntigo = pedido.Status;
        var transitou = pedido.StatusEnum == StatusPedido.AguardandoPagamento;
        if (transitou)
            // S12: pedido de exceção (ex.: aceito fora de área) passa pela aprovação da dona.
            pedido.MudarStatus(pedido.RequerAprovacao ? StatusPedido.AguardandoAprovacaoBaba : StatusPedido.Aguardando);
        await pedidoRepository.UpdateAsync(pedido, ct);
        if (transitou)
        {
            await publicador.PublicarAsync(
                empresaId, "pedido.mudou_status", "pedido", pedido.Id,
                new PedidoMudouStatusEvent(pedido.Id, empresaId, pedido.LojaId, statusAntigo, pedido.Status,
                    Origem, null, "Mercado Pago", agora),
                correlationId: pedido.Id.ToString(), ct: ct);
        }

        await publicador.PublicarAsync(
            empresaId, PedidoPagoEvent.TipoEvento, "pedido", pedido.Id,
            new PedidoPagoEvent(pedido.Id, empresaId, pedido.LojaId, pedido.ClienteId, alvo.ConversaId, alvo.Id,
                alvo.Provedor, input.PagamentoExternoId, metodo, input.ValorPago, pedido.Status, pagoEm),
            correlationId: pedido.Id.ToString(), ct: ct);

        // Registra o PedidoPagamento na mesma transação (o use case reusa a transação aberta e faz o flush).
        // Excedente permitido: o valor veio do provedor, não de digitação.
        await registrarPagamento.ExecuteAsync(new RegistrarPagamentoPedidoCommand(
            EmpresaId: empresaId,
            PedidoId: pedido.Id,
            Metodo: metodo,
            Valor: input.ValorPago,
            Referencia: input.PagamentoExternoId,
            Observacao: $"Mercado Pago, cobrança {alvo.Id}",
            RegistradoPorNome: "Mercado Pago",
            Origem: Origem,
            PermitirExcedente: true,
            ConfirmadoPeloProvedor: true), ct);

        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Pagamento confirmado pedidoId={PedidoId} cobrancaId={CobrancaId} pagamento={Pagamento} status={Status}",
            pedido.Id, alvo.Id, input.PagamentoExternoId, pedido.Status);
        var pago = new PedidoPagoOperacao(pedido.Id, pedido.Id.ToString("N")[..8].ToUpperInvariant(),
            pedido.ClienteNome, pedido.Total.Valor, pedido.AgendadoParaEm);
        return (new(SituacaoConfirmacaoPagamento.Confirmado, alvo.Id, pedido.Status), pago);
    }

    private static CobrancaPedido? EscolherCobranca(IReadOnlyList<CobrancaPedido> cobrancas, ConfirmarPagamentoPedidoInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ReferenciaExterna))
            return cobrancas.FirstOrDefault(c => c.Provedor == input.Provedor && c.ReferenciaExterna == input.ReferenciaExterna);

        var online = cobrancas.Where(c => c.Provedor == input.Provedor).ToList();
        return online.FirstOrDefault(c => c.EstaPendente) ?? online.OrderByDescending(c => c.CriadaEm).FirstOrDefault();
    }

    private async Task<ConfirmarPagamentoPedidoResult> RecusarAsync(
        PedidoEntity pedido, CobrancaPedido alvo, string motivo, SituacaoConfirmacaoPagamento situacao, DateTime agora, CancellationToken ct)
    {
        alvo.RegistrarMotivo(motivo, agora);
        await pedidoRepository.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "pagamento_divergente",
            Detalhes = motivo,
            UsuarioNome = "Mercado Pago",
            Origem = Origem,
            OcorridoEm = agora,
        }, ct);
        await unitOfWork.CommitAsync();

        logger.LogWarning("Pagamento nao confirmado pedidoId={PedidoId} cobrancaId={CobrancaId} motivo={Motivo}",
            pedido.Id, alvo.Id, motivo);
        return new(situacao, alvo.Id, pedido.Status);
    }
}
