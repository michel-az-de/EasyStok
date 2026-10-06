using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <param name="PagamentoId">Pagamento a desfazer; sem ele, o mais recente do pedido.</param>
public sealed record DesfazerPagamentoManualInput(
    Guid EmpresaId, Guid PedidoId, string Motivo, Guid? UsuarioId = null, string? UsuarioNome = null, Guid? PagamentoId = null);

public sealed record DesfazerPagamentoManualResult(Guid PedidoId, string Status, Guid PagamentoRemovidoId);

/// <summary>
/// Desfaz o "marcar como pago" registrado à mão por engano (S11). Motivo obrigatório; só pagamento
/// manual (sem pagamento do Mercado Pago por trás: esse é estorno, S27, 409 <c>use_estorno</c>) e só com
/// o preparo ainda não iniciado (409 <c>preparo_iniciado</c>). Remove o pagamento e grava a trilha com
/// usuário e motivo.
///
/// <para>
/// O pedido volta a <c>AguardandoPagamento</c> quando fica sem pagamento e tem link online pendente: é o
/// estado em que o job e o webhook voltam a cuidar dele. Pedido combinado para pagar na entrega (ou do
/// balcão, sem cobrança online) continua na fila, porque em <c>AguardandoPagamento</c> ninguém o
/// tiraria de lá.
/// </para>
/// </summary>
public sealed class DesfazerPagamentoManualUseCase(
    IPedidoStorefrontRepository pedidoStorefrontRepository,
    IPedidoRepository pedidoRepository,
    ICobrancaPedidoRepository cobrancaRepository,
    IPublicadorEventoIntegracao publicador,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    QuitacaoPedido quitacao)
{
    public Task<DesfazerPagamentoManualResult> ExecuteAsync(DesfazerPagamentoManualInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");
        if (string.IsNullOrWhiteSpace(input.Motivo))
            throw new UseCaseValidationException("Motivo é obrigatório para desfazer o pagamento.");

        return unitOfWork.ExecuteInTransactionSemRetryAsync(token => DesfazerNoLockAsync(input, token), ct);
    }

    private async Task<DesfazerPagamentoManualResult> DesfazerNoLockAsync(DesfazerPagamentoManualInput input, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var travado = await pedidoStorefrontRepository.GetForUpdateAsync(input.PedidoId, ct);
        if (travado is null || travado.EmpresaId != input.EmpresaId)
            throw new CobrancaPedidoNaoEncontradoException(input.PedidoId);

        // Com o lock na mão, carrega o agregado com os pagamentos (mesma instância rastreada).
        var pedido = await pedidoRepository.GetByIdWithDetailsAsync(input.EmpresaId, input.PedidoId)
            ?? throw new CobrancaPedidoNaoEncontradoException(input.PedidoId);

        var pagamento = input.PagamentoId is { } id
            ? pedido.Pagamentos.FirstOrDefault(p => p.Id == id)
            : pedido.Pagamentos.OrderByDescending(p => p.PagoEm).FirstOrDefault();
        if (pagamento is null)
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.SemPagamento, "Pedido sem pagamento para desfazer.");

        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(input.EmpresaId, pedido.Id, ct);
        if (!string.IsNullOrEmpty(pagamento.Referencia)
            && cobrancas.Any(c => c.EhOnline && c.PagamentoExternoId == pagamento.Referencia))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.UseEstorno,
                "Pagamento do Mercado Pago não se desfaz: use o estorno.");

        if (!PedidoStateMachine.PodeDesfazerPagamento(pedido.StatusEnum))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PreparoIniciado,
                "O preparo já começou (ou o pedido não está na fila): o pagamento não se desfaz por aqui.");

        // Remoção rastreada (mesmo padrão de RemoverPagamentoPedidoUseCase, #768): o DELETE entra no commit.
        pedido.Pagamentos.Remove(pagamento);

        var statusAntigo = pedido.Status;
        var voltaAoLink = pedido.Pagamentos.Count == 0 && cobrancas.Any(c => c.EstaPendente && c.EhOnline);
        if (voltaAoLink)
            pedido.VoltarParaAguardandoPagamento();

        await pedidoRepository.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "pagamento_desfeito",
            StatusAntigo = statusAntigo,
            StatusNovo = pedido.Status,
            Detalhes = $"-{pagamento.Valor.ToString("C", Cultura.PtBr)} ({pagamento.Metodo}). Motivo: {input.Motivo.Trim()}",
            UsuarioId = input.UsuarioId,
            UsuarioNome = input.UsuarioNome,
            Origem = "web",
            OcorridoEm = agora,
        });

        if (voltaAoLink)
        {
            await publicador.PublicarAsync(
                input.EmpresaId, "pedido.mudou_status", "pedido", pedido.Id,
                new PedidoMudouStatusEvent(pedido.Id, input.EmpresaId, pedido.LojaId, statusAntigo, pedido.Status,
                    "web", input.UsuarioId, input.UsuarioNome, agora),
                correlationId: pedido.Id.ToString(), ct: ct);
        }

        await quitacao.ReabrirManualAsync(pedido, agora, ct);
        await unitOfWork.CommitAsync();
        return new DesfazerPagamentoManualResult(pedido.Id, pedido.Status, pagamento.Id);
    }
}
