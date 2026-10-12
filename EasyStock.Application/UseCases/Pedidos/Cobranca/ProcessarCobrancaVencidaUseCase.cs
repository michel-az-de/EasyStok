using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

public enum ResultadoExpiracaoCobranca
{
    /// <summary>A cobrança já não está pendente ou ainda não venceu.</summary>
    Ignorada = 1,

    /// <summary>Cobrança expirada; o pedido já tinha saído de <c>AguardandoPagamento</c>.</summary>
    Expirada = 2,

    /// <summary>Tentativa 2 gerada e link enviado na conversa.</summary>
    Reemitida = 3,

    /// <summary>Pedido cancelado com motivo <c>pagamento_expirado</c> e vaga liberada.</summary>
    PedidoCancelado = 4,

    /// <summary>S32: o Mercado Pago tinha um pagamento aprovado (webhook perdido); o pedido foi confirmado.</summary>
    ConfirmadaPelaConsulta = 5,
}

/// <summary>
/// Regra do <c>CobrancaPedidoJob</c> para uma cobrança vencida (S11). Roda no escopo do job, um item por
/// vez, com o tenant da cobrança ligado.
///
/// <list type="bullet">
///   <item>Pedido fora de <c>AguardandoPagamento</c> (pago, na fila na entrega, cancelado): só expira a cobrança.</item>
///   <item>Tentativa 1 e pedido da conversa: gera a tentativa 2 e envia o link na conversa.</item>
///   <item>Tentativa 2, ou pedido do site (sem conversa): <see cref="CancelarPedidoUseCase"/> com motivo
///     <c>pagamento_expirado</c>, libera a vaga (pelo próprio <see cref="CancelarPedidoUseCase"/>, #1506) e avisa
///     na conversa quando houver. Aviso ao cliente do site: S13.</item>
/// </list>
///
/// <para>
/// S32: antes de expirar, consulta <c>GET v1/payments/search?external_reference=</c> para pegar webhook
/// perdido; pagamento aprovado confirma pelo <see cref="ConfirmarPagamentoPedidoUseCase"/> e nada expira.
/// Falha nessa consulta propaga: sem saber se o cliente pagou, o job não cancela e tenta na próxima rodada.
/// </para>
/// </summary>
public sealed class ProcessarCobrancaVencidaUseCase(
    IPedidoStorefrontRepository pedidoRepository,
    ICobrancaPedidoRepository cobrancaRepository,
    GerarCobrancaPedidoUseCase gerarCobranca,
    CancelarPedidoUseCase cancelarPedido,
    AvisoCobrancaConversa aviso,
    IMercadoPagoClient mercadoPagoClient,
    ConfirmarPagamentoPedidoUseCase confirmarPagamento,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<ProcessarCobrancaVencidaUseCase> logger,
    IOperacaoEventPublisher operacaoEventos)
{
    public const string MotivoCancelamento = "pagamento_expirado";

    public async Task<ResultadoExpiracaoCobranca> ExecuteAsync(CobrancaPedidoVencida item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        tenantContext.SetCurrentTenant(item.EmpresaId);

        if (await ConfirmarPagamentoPerdidoAsync(item, ct))
        {
            logger.LogInformation("Cobranca vencida confirmada pela consulta cobrancaId={CobrancaId} pedidoId={PedidoId}",
                item.CobrancaId, item.PedidoId);
            return ResultadoExpiracaoCobranca.ConfirmadaPelaConsulta;
        }

        var (resultado, conversaId, link) = await unitOfWork.ExecuteInTransactionSemRetryAsync(
            async token => await ProcessarNoLockAsync(item, token), ct);

        if (resultado == ResultadoExpiracaoCobranca.PedidoCancelado)
            await operacaoEventos.PublicarAsync(EventosOperacao.PedidoMudouStatus, item.EmpresaId,
                new PedidoMudouStatusOperacao(item.PedidoId, StatusPedidoMapper.AguardandoPagamento, StatusPedidoMapper.Cancelado), ct);

        var agora = relogio.GetUtcNow().UtcDateTime;
        if (conversaId is { } conversa)
        {
            if (resultado == ResultadoExpiracaoCobranca.Reemitida && link is not null)
                await aviso.EnviarAsync(item.EmpresaId, conversa, AvisoCobrancaConversa.TextoNovoLink(link), agora, ct);
            else if (resultado == ResultadoExpiracaoCobranca.PedidoCancelado)
                await aviso.EnviarAsync(item.EmpresaId, conversa, AvisoCobrancaConversa.TextoPedidoCancelado, agora, ct);
        }

        logger.LogInformation("Cobranca vencida cobrancaId={CobrancaId} pedidoId={PedidoId} resultado={Resultado}",
            item.CobrancaId, item.PedidoId, resultado);
        return resultado;
    }

    /// <summary>Pagamento aprovado no Mercado Pago que o webhook não entregou, do mais novo para o mais antigo.</summary>
    private async Task<bool> ConfirmarPagamentoPerdidoAsync(CobrancaPedidoVencida item, CancellationToken ct)
    {
        var pagamentos = await mercadoPagoClient.BuscarPagamentosPorReferenciaAsync(item.PedidoId.ToString(), ct);
        foreach (var pagamento in pagamentos.Where(p => p.Aprovado))
        {
            var r = await confirmarPagamento.ExecuteAsync(new ConfirmarPagamentoPedidoInput(
                item.PedidoId, pagamento.Id, pagamento.Status, pagamento.TransactionAmount,
                pagamento.PaymentMethodId, pagamento.PaymentTypeId, pagamento.DateApproved), ct);
            if (r.Confirmado) return true;
        }
        return false;
    }

    private async Task<(ResultadoExpiracaoCobranca, Guid?, string?)> ProcessarNoLockAsync(
        CobrancaPedidoVencida item, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var pedido = await pedidoRepository.GetForUpdateAsync(item.PedidoId, ct);
        if (pedido is null || pedido.EmpresaId != item.EmpresaId)
            return (ResultadoExpiracaoCobranca.Ignorada, null, null);

        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(item.EmpresaId, item.PedidoId, ct);
        var cobranca = cobrancas.FirstOrDefault(c => c.Id == item.CobrancaId);
        if (cobranca is null || !cobranca.Venceu(agora))
            return (ResultadoExpiracaoCobranca.Ignorada, null, null);

        cobranca.Expirar(agora);

        if (pedido.StatusEnum != StatusPedido.AguardandoPagamento)
        {
            await unitOfWork.CommitAsync();
            return (ResultadoExpiracaoCobranca.Expirada, null, null);
        }

        if (cobranca.Tentativa < CobrancaPedido.TentativaMaxima && cobranca.ConversaId is { } conversaId)
        {
            var nova = await gerarCobranca.ExecuteAsync(
                new GerarCobrancaPedidoInput(item.EmpresaId, item.PedidoId, conversaId, cobranca.Tentativa + 1), ct);
            return (ResultadoExpiracaoCobranca.Reemitida, conversaId, nova.LinkPagamento);
        }

        await cancelarPedido.ExecuteAsync(new CancelarPedidoCommand(
            item.EmpresaId, item.PedidoId, UsuarioNome: "Sistema", Motivo: MotivoCancelamento, Origem: "sistema"),
            publicarOperacao: false); // O evento de UI espera o commit da transação externa.
        return (ResultadoExpiracaoCobranca.PedidoCancelado, cobranca.ConversaId, null);
    }
}
