using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>Pagamento consultado na fonte cujo desfecho não confirma o pedido (S32).</summary>
/// <param name="PedidoId"><c>external_reference</c> do pagamento.</param>
/// <param name="PagamentoExternoId"><c>id</c> do pagamento no Mercado Pago.</param>
/// <param name="StatusPagamento"><c>status</c> do pagamento (<c>refunded</c>, <c>charged_back</c>, <c>rejected</c>, <c>cancelled</c>,
/// <c>in_mediation</c>).</param>
/// <param name="StatusDetalhe"><c>status_detail</c> do pagamento (ex.: <c>expired</c> no Pix vencido).</param>
public sealed record AtualizarCobrancaPorPagamentoInput(
    Guid PedidoId, string PagamentoExternoId, string StatusPagamento, string? StatusDetalhe = null);

public enum SituacaoAtualizacaoCobranca
{
    /// <summary>Status sem desfecho para a cobrança (<c>pending</c>, <c>in_process</c>, <c>approved</c>).</summary>
    Ignorada = 1,
    Estornada = 2,
    Recusada = 3,
    /// <summary>O mesmo desfecho do mesmo pagamento já estava gravado (webhook reenviado).</summary>
    JaRegistrado = 4,
    SemCobranca = 5,
    PedidoNaoEncontrado = 6,
    /// <summary><c>in_mediation</c>: o cliente abriu disputa no provedor. Registrado e a equipe avisada (#1724-12).</summary>
    EmMediacao = 7,
}

/// <summary>
/// Desfechos do pagamento do Mercado Pago que não confirmam o pedido (S32). Nunca muda o status do pedido e
/// nunca lança por regra de negócio: o processor do webhook responde 200.
///
/// <list type="bullet">
///   <item><c>refunded</c> ou <c>charged_back</c>: a cobrança paga por este pagamento vira <c>Estornada</c> e a
///     trilha ganha <c>pagamento_estornado</c>. O que fazer com o pedido é da ocorrência (S27).</item>
///   <item><c>rejected</c> ou <c>cancelled</c>: o motivo vai para a cobrança online (a pendente ou a mais recente)
///     e, com cobrança pendente vinda da conversa, o cliente recebe um aviso com o mesmo link. O mesmo pagamento
///     recusado de novo não avisa duas vezes.</item>
///   <item><c>in_mediation</c> (#1724-12): o cliente abriu disputa. A cobrança paga por este pagamento guarda o motivo,
///     a trilha ganha <c>pagamento_em_mediacao</c> e a equipe é avisada pelo sino, uma vez por pagamento. O pedido e o
///     dinheiro ficam como estão até o desfecho da disputa (a devolução chega como <c>charged_back</c>).</item>
/// </list>
/// </summary>
public sealed class AtualizarCobrancaPorPagamentoUseCase(
    ICobrancaPedidoRepository cobrancaRepository,
    IPedidoStorefrontRepository pedidoRepository,
    AvisoCobrancaConversa aviso,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<AtualizarCobrancaPorPagamentoUseCase> logger,
    INotificadorService notificador)
{
    private const string Origem = "mercadopago";

    /// <summary><c>status_detail</c> do Mercado Pago para o Pix que venceu sem pagamento.</summary>
    public const string StatusDetalheExpirado = "expired";

    public async Task<SituacaoAtualizacaoCobranca> ExecuteAsync(AtualizarCobrancaPorPagamentoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");
        if (string.IsNullOrWhiteSpace(input.PagamentoExternoId))
            throw new UseCaseValidationException("PagamentoExternoId é obrigatório.");

        var estorno = PagamentoMercadoPago.EhEstorno(input.StatusPagamento);
        if (!estorno && !PagamentoMercadoPago.EhRecusa(input.StatusPagamento) && !PagamentoMercadoPago.EhMediacao(input.StatusPagamento))
            return SituacaoAtualizacaoCobranca.Ignorada;

        var empresaId = await cobrancaRepository.ObterEmpresaIdDoPedidoAsync(input.PedidoId, ct);
        if (empresaId is null)
            return SituacaoAtualizacaoCobranca.SemCobranca;
        tenantContext.SetCurrentTenant(empresaId.Value);

        var (situacao, conversaId, link) = await unitOfWork.ExecuteInTransactionSemRetryAsync(
            token => AtualizarNoLockAsync(input, estorno, empresaId.Value, token), ct);

        if (situacao == SituacaoAtualizacaoCobranca.Recusada && conversaId is { } conversa)
            await aviso.EnviarAsync(empresaId.Value, conversa, AvisoCobrancaConversa.TextoPagamentoRecusado(link),
                relogio.GetUtcNow().UtcDateTime, ct);

        logger.LogInformation("Cobranca atualizada por pagamento pedidoId={PedidoId} situacao={Situacao}",
            input.PedidoId, situacao);
        return situacao;
    }

    private async Task<(SituacaoAtualizacaoCobranca, Guid?, string?)> AtualizarNoLockAsync(
        AtualizarCobrancaPorPagamentoInput input, bool estorno, Guid empresaId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var pedido = await pedidoRepository.GetForUpdateAsync(empresaId, input.PedidoId, ct);
        if (pedido is null || pedido.EmpresaId != empresaId)
            return (SituacaoAtualizacaoCobranca.PedidoNaoEncontrado, null, null);

        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(empresaId, pedido.Id, ct);
        var status = input.StatusPagamento.Trim().ToLowerInvariant();

        if (PagamentoMercadoPago.EhMediacao(status))
            return await RegistrarMediacaoAsync(pedido, cobrancas, input.PagamentoExternoId, status, agora, ct);

        if (estorno)
        {
            var paga = cobrancas.FirstOrDefault(c => c.PagamentoExternoId == input.PagamentoExternoId);
            if (paga is null) return (SituacaoAtualizacaoCobranca.SemCobranca, null, null);
            if (paga.Status == StatusCobrancaPedido.Estornada) return (SituacaoAtualizacaoCobranca.JaRegistrado, null, null);
            if (paga.Status != StatusCobrancaPedido.Paga) return (SituacaoAtualizacaoCobranca.SemCobranca, null, null);

            var motivoEstorno = $"estorno_mp: pagamento {input.PagamentoExternoId} ({status})";
            paga.MarcarEstornada(motivoEstorno, agora);
            await RegistrarNaTrilhaAsync(pedido.Id, "pagamento_estornado", motivoEstorno, agora, ct);
            await unitOfWork.CommitAsync();
            return (SituacaoAtualizacaoCobranca.Estornada, null, null);
        }

        var online = cobrancas.Where(c => c.Provedor == CobrancaPedido.ProvedorMercadoPago).ToList();
        var alvo = online.FirstOrDefault(c => c.EstaPendente) ?? online.OrderByDescending(c => c.CriadaEm).FirstOrDefault();
        if (alvo is null) return (SituacaoAtualizacaoCobranca.SemCobranca, null, null);
        // Recusa que chega depois do pagamento aprovado (outra tentativa, fora de ordem) não toca a cobrança paga.
        if (alvo.Status is StatusCobrancaPedido.Paga or StatusCobrancaPedido.Estornada)
            return (SituacaoAtualizacaoCobranca.Ignorada, null, null);

        var motivo = $"pagamento_recusado: pagamento {input.PagamentoExternoId} ({status})";
        if (alvo.Motivo == motivo) return (SituacaoAtualizacaoCobranca.JaRegistrado, null, null);

        alvo.RegistrarMotivo(motivo, agora);
        await RegistrarNaTrilhaAsync(pedido.Id, "pagamento_recusado", motivo, agora, ct);
        await unitOfWork.CommitAsync();

        // #1289: Pix vencido chega como cancelled no instante do vencimento, antes de o job (60 s) expirar a
        // cobrança. "Tente de novo no mesmo link" mandaria um link morto; o job cuida do link novo ou do cancelamento.
        var linkVencido = alvo.Venceu(agora)
            || string.Equals(input.StatusDetalhe?.Trim(), StatusDetalheExpirado, StringComparison.OrdinalIgnoreCase);
        return alvo.EstaPendente && !linkVencido
            ? (SituacaoAtualizacaoCobranca.Recusada, alvo.ConversaId, alvo.LinkPagamento)
            : (SituacaoAtualizacaoCobranca.Recusada, null, null);
    }

    private async Task<(SituacaoAtualizacaoCobranca, Guid?, string?)> RegistrarMediacaoAsync(EasyStock.Domain.Entities.Pedido pedido,
        IReadOnlyList<CobrancaPedido> cobrancas, string pagamentoExternoId, string status, DateTime agora, CancellationToken ct)
    {
        var paga = cobrancas.FirstOrDefault(c => c.PagamentoExternoId == pagamentoExternoId);
        if (paga is null) return (SituacaoAtualizacaoCobranca.SemCobranca, null, null);
        if (paga.Status != StatusCobrancaPedido.Paga) return (SituacaoAtualizacaoCobranca.Ignorada, null, null);
        var motivo = $"em_mediacao: pagamento {pagamentoExternoId} ({status})";
        if (paga.Motivo?.Contains(motivo, StringComparison.Ordinal) == true) return (SituacaoAtualizacaoCobranca.JaRegistrado, null, null);

        // Acrescenta ao motivo que já existe: as marcas do começo (valor menor, estorno automático) continuam valendo.
        paga.RegistrarMotivo(paga.Motivo is null ? motivo : $"{paga.Motivo} | {motivo}", agora);
        await RegistrarNaTrilhaAsync(pedido.Id, "pagamento_em_mediacao", motivo, agora, ct);
        // Sem usuarioId: vale a audiência da rotina (gestores). Só primeiro nome e referência, como os avisos do Balcão.
        var correlacao = CorrelacaoAvisoMediacao(pagamentoExternoId);
        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["nome"] = AvisoEquipeNoAparelho.PrimeiroNome(pedido.ClienteNome),
            ["referencia"] = PrazoEstouradoEvento.Referencia(pedido.Id),
            [NotificadorService.ChaveIdempotenciaPayload] = correlacao,
        });
        await notificador.EnfileirarEventoAsync(TipoEventoNotificacao.PagamentoEmMediacao, pedido.EmpresaId, payload,
            pedido.Id, ct, correlacao);
        await unitOfWork.CommitAsync();
        return (SituacaoAtualizacaoCobranca.EmMediacao, null, null);
    }

    public static string CorrelacaoAvisoMediacao(string pagamentoExternoId) => $"pagamento-em-mediacao:{pagamentoExternoId}";

    private Task RegistrarNaTrilhaAsync(Guid pedidoId, string tipo, string detalhes, DateTime agora, CancellationToken ct) =>
        pedidoRepository.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedidoId,
            Tipo = tipo,
            Detalhes = detalhes,
            UsuarioNome = "Mercado Pago",
            Origem = Origem,
            OcorridoEm = agora,
        }, ct);
}
