using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Atendimento.Automacoes;

public enum SituacaoLembreteCobranca
{
    /// <summary>Não é hora (ainda falta, já venceu) ou não há o que lembrar: pago, cancelado, substituído, sem conversa.</summary>
    Ignorada = 1,

    /// <summary>Esta cobrança já teve o lembrete (enviado, recusado pelas regras da automática ou falho no canal).</summary>
    JaAvisada = 2,

    /// <summary>A automática foi chamada; <see cref="LembreteCobrancaResult.Automacao"/> diz se saiu.</summary>
    Disparada = 3,
}

public sealed record LembreteCobrancaResult(SituacaoLembreteCobranca Situacao, ResultadoAutomacao? Automacao = null);

/// <summary>
/// #1610: lembra o cliente, pela conversa, que o link de pagamento vence em ~10 min. Chamado pelo
/// <c>CobrancaPedidoJob</c> a cada rodada, um item por escopo, com o tenant ligado aqui.
///
/// <para>
/// Só avisa (decisão do Felipe para link vencido, #1889): não reemite nem cancela. Não avisa se a cobrança já não está
/// pendente (paga, cancelada ou substituída por outra), se o pedido saiu de <c>AguardandoPagamento</c> (inclusive pago
/// no balcão com o link ainda aberto) ou se o link não é da conversa.
/// </para>
///
/// <para>
/// No máximo uma vez por cobrança: a reserva é um INSERT na tabela de idempotência
/// (<see cref="CobrancaPedidoVencendo.RecursoLembrete"/>, índice único) ANTES de enviar, e qualquer desfecho a encerra,
/// inclusive falha do canal. Janela de 24 h, consentimento, bloqueio e texto são do <see cref="DispararAutomacaoUseCase"/>.
/// </para>
/// </summary>
public sealed class LembrarCobrancaVencendoUseCase(
    ITenantContextAccessor tenant,
    ICobrancaPedidoRepository cobrancas,
    IPedidoRepository pedidos,
    IIdempotencyKeyRepository idempotencia,
    DispararAutomacaoUseCase disparo,
    TimeProvider relogio,
    ILogger<LembrarCobrancaVencendoUseCase> logger)
{
    /// <summary>Quanto antes do vencimento o lembrete sai. O job roda a cada 60 s: chega entre 9 e 10 min antes.</summary>
    public static readonly TimeSpan Antecedencia = TimeSpan.FromMinutes(10);

    /// <summary>A reserva só precisa viver enquanto o link pode ser lembrado; a limpeza da tabela apaga depois.</summary>
    private static readonly TimeSpan ValidadeReserva = TimeSpan.FromDays(1);

    public async Task<LembreteCobrancaResult> ExecuteAsync(CobrancaPedidoVencendo item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        tenant.SetCurrentTenant(item.EmpresaId);
        var agora = relogio.GetUtcNow().UtcDateTime;

        var cobranca = (await cobrancas.ListarDoPedidoAsync(item.EmpresaId, item.PedidoId, ct)).FirstOrDefault(c => c.Id == item.CobrancaId);
        if (cobranca?.ConversaId is not { } conversaId || !cobranca.VenceEmBreve(agora, Antecedencia))
            return new(SituacaoLembreteCobranca.Ignorada);

        var pedido = await pedidos.GetByIdAsync(item.EmpresaId, item.PedidoId);
        if (pedido is null || pedido.StatusEnum != StatusPedido.AguardandoPagamento)
            return new(SituacaoLembreteCobranca.Ignorada);

        var (reserva, reservada) = await idempotencia.TryBeginAsync(
            item.Chave, item.EmpresaId, CobrancaPedidoVencendo.RecursoLembrete, null, ValidadeReserva, ct);
        if (!reservada) return new(SituacaoLembreteCobranca.JaAvisada);

        // No máximo uma vez: falha do canal não é reenviada (o timeout pode ter entregado) e a reserva fica encerrada
        // mesmo se o disparo lançar. Um lembrete perdido é melhor que o cliente receber o mesmo aviso a cada minuto.
        ResultadoAutomacao? resultado = null;
        try
        {
            resultado = await disparo.ExecuteAsync(
                new DisparoAutomacao(item.EmpresaId, GatilhoAutomacao.CobrancaVencendo, conversaId, pedido.ClienteId, pedido.Id), ct);
        }
        finally
        {
            await idempotencia.MarcarConcluidoAsync(reserva.Id, 200, resultado?.ToString() ?? "Erro", CancellationToken.None);
        }

        logger.LogInformation("Lembrete do link cobrancaId={CobrancaId} pedidoId={PedidoId} resultado={Resultado}",
            item.CobrancaId, item.PedidoId, resultado);
        return new(SituacaoLembreteCobranca.Disparada, resultado);
    }
}
