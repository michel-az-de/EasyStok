using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <param name="Status">Status canônico do pedido (<c>StatusPedidoMapper</c>).</param>
/// <param name="Itens">Itens do cardápio, sem a linha de frete.</param>
public sealed record PedidoConversaResult(
    Guid PedidoId,
    string Status,
    decimal Total,
    decimal Frete,
    DateTime CriadoEm,
    DateTime? AgendadoParaEm,
    IReadOnlyList<ItemPedidoConversaResult> Itens,
    CobrancaPedidoConversaResult? Cobranca,
    decimal TotalPago = 0,
    IReadOnlyList<PagamentoPedidoConversaResult>? Pagamentos = null,
    bool RequerAprovacao = false);

public sealed record PagamentoPedidoConversaResult(Guid Id, decimal Valor, string Metodo, DateTime PagoEm);

public sealed record ItemPedidoConversaResult(
    Guid? CardapioItemId,
    string Nome,
    decimal Quantidade,
    decimal PrecoUnitario,
    string? Observacao);

/// <param name="Status">Nome do <see cref="StatusCobrancaPedido"/> (Pendente, Paga, Expirada, Cancelada, Estornada).</param>
public sealed record CobrancaPedidoConversaResult(
    Guid CobrancaId,
    string Provedor,
    string Status,
    string? LinkPagamento,
    decimal Valor,
    DateTime? ExpiraEm,
    DateTime? PagaEm,
    decimal? ValorPago,
    string? MetodoPagamento,
    int Tentativa,
    DateTime CriadaEm)
{
    internal static CobrancaPedidoConversaResult De(CobrancaPedido c) => new(
        c.Id, c.Provedor, c.Status.ToString(), c.LinkPagamento, c.Valor, c.ExpiraEm, c.PagaEm, c.ValorPago,
        c.MetodoPagamento, c.Tentativa, c.CriadaEm);
}

/// <summary>
/// F03 do console: o pedido em andamento da conversa com a cobrança que vale agora, para a Ficha e a
/// comanda acompanharem pelo polling (pendente, paga, expirada). A cobrança paga vence as outras; sem
/// ela, a mais recente. Conversa sem pedido devolve nulo.
/// </summary>
public sealed class ObterPedidoConversaUseCase(
    IConversaRepository conversaRepository,
    IPedidoRepository pedidoRepository,
    ICobrancaPedidoRepository cobrancaRepository)
{
    public async Task<PedidoConversaResult?> ExecuteAsync(Guid empresaId, Guid conversaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var conversa = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);
        if (conversa.PedidoEmAndamentoId is not { } pedidoId) return null;

        var pedido = await pedidoRepository.GetByIdWithDetailsAsync(empresaId, pedidoId);
        if (pedido is null) return null;

        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(empresaId, pedido.Id, ct);
        var vigente = cobrancas.FirstOrDefault(c => c.Status == StatusCobrancaPedido.Paga)
            ?? cobrancas.OrderByDescending(c => c.CriadaEm).FirstOrDefault();

        // O frete é a linha sem item de cardápio nem produto (CheckoutCoreService.AdicionarItemFreteAsync).
        var itens = pedido.Itens.Where(i => i.CardapioItemId is not null || i.ProdutoId is not null).ToList();
        var frete = pedido.Itens.Except(itens).Sum(i => i.Subtotal);

        return new PedidoConversaResult(
            pedido.Id, pedido.Status, pedido.Total.Valor, frete, pedido.CriadoEm, pedido.AgendadoParaEm,
            itens.Select(i => new ItemPedidoConversaResult(i.CardapioItemId, i.Nome, i.Quantidade, i.PrecoUnitario, i.Observacao)).ToList(),
            vigente is null ? null : CobrancaPedidoConversaResult.De(vigente), pedido.TotalPago,
            pedido.Pagamentos.Select(p => new PagamentoPedidoConversaResult(p.Id, p.Valor, p.Metodo, p.PagoEm)).ToList(),
            // #1474: o console barra a baixa manual antes de cancelar o link quando falta aprovar.
            pedido.RequerAprovacao && pedido.AprovadoEm is null);
    }
}
