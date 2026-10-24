using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;

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

    /// <summary>
    /// I3.2: a credencial atual não alcança a conta da cobrança (trocada, ausente ou recusada). Nada expira e
    /// nada é cancelado; a cobrança sai da varredura até a credencial voltar.
    /// </summary>
    AguardandoCredencial = 6,

    /// <summary>
    /// #1717-3 (decisão do Felipe, #1889): link vencido na última tentativa. A cobrança expira, o pedido continua
    /// <c>AguardandoPagamento</c> e a conversa recebe o aviso; a atendente reemite ou cancela.
    /// </summary>
    AguardandoAtendente = 7,
}

/// <summary>
/// Regra do <c>CobrancaPedidoJob</c> para uma cobrança vencida (S11). Roda no escopo do job, um item por
/// vez, com o tenant da cobrança ligado.
///
/// <list type="bullet">
///   <item>Pedido fora de <c>AguardandoPagamento</c> (pago, na fila na entrega, cancelado): só expira a cobrança.</item>
///   <item>Tentativa 1 e pedido da conversa: gera a tentativa 2 e envia o link na conversa.</item>
///   <item>Tentativa 2 e pedido da conversa (#1717-3, decisão do Felipe em #1889): não cancela. A cobrança expira,
///     o pedido fica <c>AguardandoPagamento</c> e a conversa recebe o aviso; a atendente reemite ou cancela.</item>
///   <item>Pedido do site (sem conversa): <see cref="CancelarPedidoUseCase"/> com motivo <c>pagamento_expirado</c>
///     e libera a vaga (pelo próprio <see cref="CancelarPedidoUseCase"/>, #1506). Aviso ao cliente do site: S13.</item>
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
    IOperacaoEventPublisher operacaoEventos,
    INotificadorService notificador)
{
    public const string MotivoCancelamento = "pagamento_expirado";

    public async Task<ResultadoExpiracaoCobranca> ExecuteAsync(CobrancaPedidoVencida item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        tenantContext.SetCurrentTenant(item.EmpresaId);

        if (await CredencialNaoAlcancaAsync(item, ct))
        {
            await MarcarAguardandoCredencialAsync(item, ct);
            logger.LogWarning("Cobranca vencida aguardando credencial cobrancaId={CobrancaId} pedidoId={PedidoId}",
                item.CobrancaId, item.PedidoId);
            return ResultadoExpiracaoCobranca.AguardandoCredencial;
        }

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
            else if (resultado == ResultadoExpiracaoCobranca.AguardandoAtendente)
                await aviso.EnviarAsync(item.EmpresaId, conversa, AvisoCobrancaConversa.TextoLinkVencidoAguardandoAtendente, agora, ct);
        }

        logger.LogInformation("Cobranca vencida cobrancaId={CobrancaId} pedidoId={PedidoId} resultado={Resultado}",
            item.CobrancaId, item.PedidoId, resultado);
        return resultado;
    }

    /// <summary>
    /// I3.2 (invariante 4): sem credencial, com credencial recusada ou com credencial de outra conta, a busca
    /// por pagamento voltaria vazia e o job cancelaria um pedido pago. Instabilidade do provedor (5xx, timeout)
    /// não entra aqui: propaga e o job tenta na próxima rodada.
    /// </summary>
    private async Task<bool> CredencialNaoAlcancaAsync(CobrancaPedidoVencida item, CancellationToken ct)
    {
        return await ConferirPagamentoPedidoUseCase.CredencialNaoAlcancaAsync(mercadoPagoClient, item, ct);
    }

    private async Task MarcarAguardandoCredencialAsync(CobrancaPedidoVencida item, CancellationToken ct)
    {
        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(item.EmpresaId, item.PedidoId, ct);
        var cobranca = cobrancas.FirstOrDefault(c => c.Id == item.CobrancaId);
        if (cobranca is null || !cobranca.EstaPendente) return;
        cobranca.AguardarCredencial(relogio.GetUtcNow().UtcDateTime);
        await unitOfWork.CommitAsync();
    }

    /// <summary>Pagamento aprovado no Mercado Pago que o webhook não entregou, do mais novo para o mais antigo.</summary>
    private async Task<bool> ConfirmarPagamentoPerdidoAsync(CobrancaPedidoVencida item, CancellationToken ct)
    {
        return (await ConferirPagamentoPedidoUseCase.ConsultarAsync(mercadoPagoClient, confirmarPagamento, item, ct)).Confirmado;
    }

    private async Task<(ResultadoExpiracaoCobranca, Guid?, string?)> ProcessarNoLockAsync(
        CobrancaPedidoVencida item, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var pedido = await pedidoRepository.GetForUpdateAsync(item.EmpresaId, item.PedidoId, ct);
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
                // Complementar reemite pelo saldo (#1719): como cobrança comum, o cupom já abatido voltava ao link.
                new GerarCobrancaPedidoInput(item.EmpresaId, item.PedidoId, conversaId, cobranca.Tentativa + 1,
                    CobrarSaldo: cobranca.Complementar), ct);
            return (ResultadoExpiracaoCobranca.Reemitida, conversaId, nova.LinkPagamento);
        }

        // #1717-3: pedido da conversa não é cancelado pelo job ("avisa, não trava"). Fica aguardando a atendente.
        // #1889 (decisão do Felipe, 21/10): a equipe é chamada pelo sino na mesma transação; com a conversa encerrada
        // o aviso ao cliente não sai, e sem isso ninguém ficava sabendo.
        // #1724-7: o pedido do site com pagamento a menor registrado tem dinheiro recebido; o sistema não o cancela
        // (exigiria a dona) e a equipe decide o acerto: cobrar o restante ou devolver.
        if (cobranca.ConversaId is not null || cobrancas.Any(c => c.RegistraPagamentoAMenor && c.Status == StatusCobrancaPedido.Paga))
        {
            await AvisarEquipeAsync(pedido, cobranca, ct);
            await unitOfWork.CommitAsync();
            return (ResultadoExpiracaoCobranca.AguardandoAtendente, cobranca.ConversaId, null);
        }

        await cancelarPedido.ExecuteAsync(new CancelarPedidoCommand(
            item.EmpresaId, item.PedidoId, UsuarioNome: "Sistema", Motivo: MotivoCancelamento, Origem: "sistema"),
            publicarOperacao: false); // O evento de UI espera o commit da transação externa.
        return (ResultadoExpiracaoCobranca.PedidoCancelado, cobranca.ConversaId, null);
    }

    public static string CorrelacaoAvisoEquipe(Guid cobrancaId) => $"cobranca-aguardando-equipe:{cobrancaId:N}";

    // Sem usuarioId: vale a audiência da rotina. Só primeiro nome e referência, como os avisos do Balcão.
    private Task AvisarEquipeAsync(EasyStock.Domain.Entities.Pedido pedido, CobrancaPedido cobranca, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["nome"] = AvisoEquipeNoAparelho.PrimeiroNome(pedido.ClienteNome),
            ["referencia"] = CodigoCurto.De(pedido.Id),
            [NotificadorService.ChaveIdempotenciaPayload] = CorrelacaoAvisoEquipe(cobranca.Id),
        });
        return notificador.EnfileirarEventoAsync(TipoEventoNotificacao.CobrancaAguardandoEquipe, pedido.EmpresaId, payload,
            pedido.Id, ct, CorrelacaoAvisoEquipe(cobranca.Id));
    }
}
