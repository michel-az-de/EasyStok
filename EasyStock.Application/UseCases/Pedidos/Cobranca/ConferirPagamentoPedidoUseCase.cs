using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

public sealed record ConferirPagamentoPedidoResult(string Situacao, bool Confirmado);

public interface IConferenciaPagamentoPedido
{
    Task<ConferirPagamentoPedidoResult> ExecuteAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default);
}

public sealed class ConferirPagamentoPedidoUseCase(
    IPedidoRepository pedidos,
    ICobrancaPedidoRepository cobrancas,
    IMercadoPagoClient mercadoPago,
    ConfirmarPagamentoPedidoUseCase confirmar) : IConferenciaPagamentoPedido
{
    public const string SituacaoConfirmado = "confirmado";
    public const string SituacaoJaConferido = "ja_conferido";
    public const string SituacaoNaoEncontrado = "nao_encontrado";
    public const string SituacaoEmProcessamento = "em_processamento";
    public const string SituacaoNaoConciliado = "nao_conciliado";
    public const string SituacaoAguardandoCredencial = "aguardando_credencial";
    public const string SituacaoSemCobrancaOnline = "sem_cobranca_online";

    public async Task<ConferirPagamentoPedidoResult> ExecuteAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(pedidoId, "PedidoId");
        var pedido = await pedidos.GetByIdWithDetailsAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId)
            throw new CobrancaPedidoNaoEncontradoException(pedidoId);
        if (PedidoStateMachine.EstaFinalizado(pedido.StatusEnum))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoFinalizado,
                "Pedido cancelado ou entregue não permite conferir pagamento.");

        var lista = await cobrancas.ListarDoPedidoAsync(empresaId, pedidoId, ct);
        var pendente = lista.FirstOrDefault(c => c.EmpresaId == empresaId && c.PedidoId == pedidoId && c.EhOnline && c.EstaPendente);
        if (pendente is null)
            return lista.Any(c => c.EmpresaId == empresaId && c.PedidoId == pedidoId && c.EhOnline && c.Status == StatusCobrancaPedido.Paga)
                ? new(SituacaoJaConferido, true) : new(SituacaoSemCobrancaOnline, false);

        var item = new CobrancaPedidoVencida(pendente.Id, empresaId, pedidoId, pendente.ContaRecebedoraId);
        try
        {
            if (await CredencialNaoAlcancaAsync(mercadoPago, item, ct))
                return new(SituacaoAguardandoCredencial, false);
            return await ConsultarAsync(mercadoPago, confirmar, item, ct);
        }
        catch (MercadoPagoNaoConfiguradoException)
        {
            return new(SituacaoAguardandoCredencial, false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return new(SituacaoAguardandoCredencial, false);
        }
        catch (HttpRequestException ex)
        {
            throw new MercadoPagoIndisponivelException("Não foi possível conferir o pagamento. Tente novamente em instantes.", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MercadoPagoIndisponivelException("Não foi possível conferir o pagamento. Tente novamente em instantes.", ex);
        }
    }

    // Compartilhado com o job: consulta sem alcançar a conta original não prova ausência de pagamento.
    internal static async Task<bool> CredencialNaoAlcancaAsync(IMercadoPagoClient mercadoPago, CobrancaPedidoVencida item, CancellationToken ct)
    {
        string contaAtual;
        try { contaAtual = await mercadoPago.ObterContaRecebedoraAsync(ct); }
        catch (MercadoPagoNaoConfiguradoException) { return true; }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        { return true; }
        return item.ContaRecebedoraId is { } conta && !string.Equals(conta, contaAtual, StringComparison.Ordinal);
    }

    internal static async Task<ConferirPagamentoPedidoResult> ConsultarAsync(IMercadoPagoClient mercadoPago,
        ConfirmarPagamentoPedidoUseCase confirmar, CobrancaPedidoVencida item, CancellationToken ct)
    {
        var pagamentos = await mercadoPago.BuscarPagamentosPorReferenciaAsync(item.PedidoId.ToString(), ct);
        foreach (var pagamento in pagamentos.Where(p => p.Aprovado))
        {
            var resultado = await confirmar.ExecuteAsync(new ConfirmarPagamentoPedidoInput(
                item.PedidoId, pagamento.Id, pagamento.Status, pagamento.TransactionAmount,
                pagamento.PaymentMethodId, pagamento.PaymentTypeId, pagamento.DateApproved,
                ContaRecebedoraId: pagamento.CollectorId), ct);
            if (resultado.Confirmado) return new(SituacaoConfirmado, true);
        }
        // Recusado, cancelado ou devolvido não está em processamento: só tentativa sem dinheiro equivale a nada pago.
        return new(pagamentos.Any(p => p.Aprovado) ? SituacaoNaoConciliado
            : pagamentos.Any(p => !p.Recusado && !p.Estornado) ? SituacaoEmProcessamento : SituacaoNaoEncontrado, false);
    }
}
