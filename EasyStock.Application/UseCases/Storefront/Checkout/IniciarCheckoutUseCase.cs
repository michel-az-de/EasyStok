using System.Diagnostics;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Checkout Storefront — protocolo de 3 fases (ADR-0014). As fases 1 e 2 vivem no
/// <see cref="CheckoutCoreService"/> (S10), o mesmo caminho do pedido feito na conversa.
///
/// <para>
/// <strong>Fase 1 (transação curta):</strong> Cria <c>Pedido(Rascunho)</c> com itens,
/// janela escolhida e valor total. Persiste sem reservar vaga ainda.
/// </para>
///
/// <para>
/// <strong>Fase 2 (transação curta, crítica):</strong>
/// <see cref="IVagaOcupadaRepository.OcuparAsync"/> INSERT atômico com advisory lock.
/// Falha com <see cref="JanelaSemVagasException"/> → 409 + janelas alternativas.
/// Sucesso → <c>Pedido.status = AguardandoPagamento</c>.
/// </para>
///
/// <para>
/// <strong>Fase 3 (fora de transação):</strong>
/// <see cref="GerarCobrancaPedidoUseCase"/> (S11): preferência MP com expiração de 30 min (timeout 5 s)
/// e <c>CobrancaPedido</c> gravada. Falha → Pedido fica AguardandoPagamento; o <c>CobrancaPedidoJob</c>
/// expira a cobrança e cancela o pedido.
/// Sucesso → retorna <c>{pedidoId, initPointUrl, expiresIn}</c>.
/// </para>
/// </summary>
public sealed class IniciarCheckoutUseCase(
    CheckoutCoreService checkoutCore,
    CheckoutIdempotencyService idempotencyService,
    GerarCobrancaPedidoUseCase gerarCobranca,
    ILogger<IniciarCheckoutUseCase> logger)
{
    private const int ExpiresInSeconds = 1800;

    public async Task<CheckoutCriadoDto> ExecuteAsync(
        IniciarCheckoutInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sw = Stopwatch.StartNew();

        // ── Validações iniciais ──────────────────────────────────────────────

        CheckoutCoreService.ValidarEntrada(input.Cep, input.Items?.Select(i => (i.CardapioItemId, i.Qtd)).ToList());

        // ── Idempotência (verificação antecipada) ─────────────────────────
        if (input.IdempotencyKey.HasValue && input.ContentHash is not null)
        {
            var cached = await idempotencyService.TentarReservarAsync(
                input.IdempotencyKey.Value, input.ContentHash, ct);
            if (cached is not null)
                return cached;
        }

        // ═══════════════════════════════════════════════════════════════════
        // FASES 1 e 2 — Pedido (Rascunho) + vaga → AguardandoPagamento (S10)
        // ═══════════════════════════════════════════════════════════════════

        var reservado = await checkoutCore.CriarPedidoComReservaAsync(
            new CheckoutCoreInput(
                ClienteId: input.ClienteId,
                Itens: input.Items!.Select(i => new ItemPedidoCheckout(i.CardapioItemId, i.Qtd)).ToList(),
                JanelaId: input.JanelaId,
                DataEntrega: input.DataEntrega,
                Cep: input.Cep,
                Origem: OrigemPedido.Storefront,
                Slug: input.Slug,
                Observacoes: input.Observacoes),
            ct);
        var pedido = reservado.Pedido;
        var storefront = reservado.Storefront;

        // ═══════════════════════════════════════════════════════════════════
        // FASE 3 — Criar Preference MP (fora de transação, timeout 5 s)
        // ═══════════════════════════════════════════════════════════════════

        // S11: a cobrança passa pelo mesmo use case da conversa e grava a CobrancaPedido. Falha ou timeout
        // do MP → MercadoPagoIndisponivelException e o pedido fica AguardandoPagamento (o job expira).
        var cobranca = await gerarCobranca.ExecuteAsync(reservado, conversaId: null, ct);
        var initPointUrl = cobranca.LinkPagamento!;

        // Registrar resposta de idempotência
        if (input.IdempotencyKey.HasValue && input.ContentHash is not null)
        {
            await idempotencyService.RegistrarRespostaAsync(
                input.IdempotencyKey.Value, input.ContentHash,
                pedido.Id, initPointUrl, ct);
        }

        logger.LogInformation(
            "Checkout fase-3 ok pedidoId={PedidoId} storefrontId={StorefrontId} totalMs={Ms}ms",
            pedido.Id, storefront.Id, sw.ElapsedMilliseconds);

        return new CheckoutCriadoDto(pedido.Id, initPointUrl, ExpiresInSeconds);
    }
}
