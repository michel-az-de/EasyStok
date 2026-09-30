using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>Pagamento consultado na fonte cujo desfecho não confirma o pedido (S32).</summary>
/// <param name="PedidoId"><c>external_reference</c> do pagamento.</param>
/// <param name="PagamentoExternoId"><c>id</c> do pagamento no Mercado Pago.</param>
/// <param name="StatusPagamento"><c>status</c> do pagamento (<c>refunded</c>, <c>charged_back</c>, <c>rejected</c>, <c>cancelled</c>).</param>
public sealed record AtualizarCobrancaPorPagamentoInput(Guid PedidoId, string PagamentoExternoId, string StatusPagamento);

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
/// </list>
/// </summary>
public sealed class AtualizarCobrancaPorPagamentoUseCase(
    ICobrancaPedidoRepository cobrancaRepository,
    IPedidoStorefrontRepository pedidoRepository,
    AvisoCobrancaConversa aviso,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<AtualizarCobrancaPorPagamentoUseCase> logger)
{
    private const string Origem = "mercadopago";

    public async Task<SituacaoAtualizacaoCobranca> ExecuteAsync(AtualizarCobrancaPorPagamentoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");
        if (string.IsNullOrWhiteSpace(input.PagamentoExternoId))
            throw new UseCaseValidationException("PagamentoExternoId é obrigatório.");

        var estorno = PagamentoMercadoPago.EhEstorno(input.StatusPagamento);
        if (!estorno && !PagamentoMercadoPago.EhRecusa(input.StatusPagamento))
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
        var pedido = await pedidoRepository.GetForUpdateAsync(input.PedidoId, ct);
        if (pedido is null || pedido.EmpresaId != empresaId)
            return (SituacaoAtualizacaoCobranca.PedidoNaoEncontrado, null, null);

        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(empresaId, pedido.Id, ct);
        var status = input.StatusPagamento.Trim().ToLowerInvariant();

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

        var motivo = $"pagamento_recusado: pagamento {input.PagamentoExternoId} ({status})";
        if (alvo.Motivo == motivo) return (SituacaoAtualizacaoCobranca.JaRegistrado, null, null);

        alvo.RegistrarMotivo(motivo, agora);
        await RegistrarNaTrilhaAsync(pedido.Id, "pagamento_recusado", motivo, agora, ct);
        await unitOfWork.CommitAsync();
        return alvo.EstaPendente
            ? (SituacaoAtualizacaoCobranca.Recusada, alvo.ConversaId, alvo.LinkPagamento)
            : (SituacaoAtualizacaoCobranca.Recusada, null, null);
    }

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
