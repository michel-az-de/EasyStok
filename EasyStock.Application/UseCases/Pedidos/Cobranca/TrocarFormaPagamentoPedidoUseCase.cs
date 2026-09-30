using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
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
///   <item><c>na_entrega</c>: sem link; pedido em <c>AguardandoPagamento</c> vai para <c>Aguardando</c>
///     (entra na fila e paga na entrega), senão o job o cancelaria por falta de pagamento.</item>
/// </list>
///
/// <para>
/// Pendente para S32: expirar a preferência anterior no Mercado Pago (<c>PUT checkout/preferences/{id}</c>).
/// Até lá, um pagamento que ainda chegue pelo link antigo é confirmado mesmo assim
/// (<see cref="ConfirmarPagamentoPedidoUseCase"/>: dinheiro recebido vence).
/// </para>
/// </summary>
public sealed class TrocarFormaPagamentoPedidoUseCase(
    IPedidoStorefrontRepository pedidoRepository,
    ICobrancaPedidoRepository cobrancaRepository,
    GerarCobrancaPedidoUseCase gerarCobranca,
    AvisoCobrancaConversa aviso,
    IPublicadorEventoIntegracao publicador,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
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

        var (cobranca, conversaId) = await unitOfWork.ExecuteInTransactionSemRetryAsync(
            async token => await TrocarNoLockAsync(input, forma, token), ct);

        var enviada = false;
        if (conversaId is { } conversa && cobranca.LinkPagamento is { } link)
            enviada = await aviso.EnviarAsync(input.EmpresaId, conversa, AvisoCobrancaConversa.TextoLinkTrocado(link),
                relogio.GetUtcNow().UtcDateTime, ct);

        return new TrocarFormaPagamentoPedidoResult(cobranca, enviada);
    }

    private async Task<(CobrancaPedidoResult, Guid?)> TrocarNoLockAsync(
        TrocarFormaPagamentoPedidoInput input, string forma, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var pedido = await pedidoRepository.GetForUpdateAsync(input.PedidoId, ct);
        if (pedido is null || pedido.EmpresaId != input.EmpresaId)
            throw new CobrancaPedidoNaoEncontradoException(input.PedidoId);
        if (PedidoStateMachine.EstaFinalizado(pedido.StatusEnum))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoFinalizado,
                "Pedido cancelado ou entregue não troca a forma de pagamento.");

        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(input.EmpresaId, pedido.Id, ct);
        if (cobrancas.Any(c => c.Status == StatusCobrancaPedido.Paga))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoJaPago, "Pedido já pago.");

        var pendente = cobrancas.FirstOrDefault(c => c.EstaPendente);
        var provedorPedido = forma == FormaOnline ? CobrancaPedido.ProvedorMercadoPago : CobrancaPedido.ProvedorNaEntrega;
        if (pendente is not null && pendente.Provedor == provedorPedido && !pendente.Venceu(agora))
            return (CobrancaPedidoResult.De(pendente, reutilizada: true), pendente.ConversaId);

        var conversaId = cobrancas.Where(c => c.ConversaId is not null).Select(c => c.ConversaId).LastOrDefault();
        var formaAnterior = pendente?.Provedor ?? "nenhuma";
        pendente?.Cancelar($"troca_forma: {forma}", agora);

        CobrancaPedidoResult nova;
        if (forma == FormaOnline)
        {
            var tentativa = cobrancas.Where(c => c.EhOnline).Select(c => c.Tentativa).DefaultIfEmpty(1).Max();
            nova = await gerarCobranca.ExecuteAsync(
                new GerarCobrancaPedidoInput(input.EmpresaId, pedido.Id, conversaId, tentativa), ct);
        }
        else
        {
            var naEntrega = CobrancaPedido.CriarNaEntrega(input.EmpresaId, pedido.Id, pedido.Total.Valor, agora, conversaId);
            await cobrancaRepository.AddAsync(naEntrega, ct);
            await ColocarNaFilaAsync(pedido, input, agora, ct);
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

        return (nova, conversaId);
    }

    private async Task ColocarNaFilaAsync(PedidoEntity pedido, TrocarFormaPagamentoPedidoInput input, DateTime agora, CancellationToken ct)
    {
        if (pedido.StatusEnum != StatusPedido.AguardandoPagamento) return;

        var statusAntigo = pedido.Status;
        pedido.MudarStatus(StatusPedido.Aguardando);
        await pedidoRepository.UpdateAsync(pedido, ct);
        await publicador.PublicarAsync(
            input.EmpresaId, "pedido.mudou_status", "pedido", pedido.Id,
            new PedidoMudouStatusEvent(pedido.Id, input.EmpresaId, pedido.LojaId, statusAntigo, pedido.Status,
                "web", input.UsuarioId, input.UsuarioNome, agora),
            correlationId: pedido.Id.ToString(), ct: ct);
    }
}
