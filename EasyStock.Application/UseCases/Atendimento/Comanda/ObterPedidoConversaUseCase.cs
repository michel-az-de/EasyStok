using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.AlterarAgendamentoPedido;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
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
    bool RequerAprovacao = false,
    JanelaPedidoResult? Janela = null,
    bool SinalNaReserva = false,
    decimal? Alocado = null,
    long NumeroComanda = 0,
    string? Observacoes = null);

public sealed record PagamentoPedidoConversaResult(Guid Id, decimal Valor, string Metodo, DateTime PagoEm);

/// <param name="VariacaoId">M1.4b (#1531): a porção vendida (null em prato sem porções).</param>
public sealed record ItemPedidoConversaResult(
    Guid? CardapioItemId,
    string Nome,
    decimal Quantidade,
    decimal PrecoUnitario,
    string? Observacao,
    Guid? VariacaoId = null,
    string? Porcao = null);

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
    ICobrancaPedidoRepository cobrancaRepository,
    IVagaOcupadaRepository vagas,
    SaldoFinanceiroPedido? saldos = null)
{
    public async Task<PedidoConversaResult?> ExecuteAsync(Guid empresaId, Guid conversaId, CancellationToken ct = default, Guid? pedidoEscolhido = null)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var conversa = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);
        if ((pedidoEscolhido ?? conversa.PedidoEmAndamentoId) is not { } pedidoId) return null;

        var pedido = await pedidoRepository.GetByIdWithDetailsAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId) return null;
        return await MontarAsync(conversa, pedido, saldos is null ? null : await saldos.ConsultarAsync(pedido, ct), ct);
    }

    /// <summary>
    /// O mesmo resultado com a conversa, o pedido e o saldo já lidos por quem chama (#1725-7): a comanda não relê o
    /// pedido inteiro nem refaz a conta do saldo para montar o detalhe. Pedido de outra empresa não monta.
    /// </summary>


    public async Task<PedidoConversaResult> MontarAsync(Conversa conversa, EasyStock.Domain.Entities.Pedido pedido, SaldoFinanceiroComanda? financeiro,
        CancellationToken ct = default)
    {
        if (pedido.EmpresaId != conversa.EmpresaId)
            throw new InvalidOperationException("Pedido e conversa de empresas diferentes.");
        var janela = (await vagas.GetByPedidoIdsAsync([pedido.Id], ct)).GetValueOrDefault(pedido.Id);

        // #1724-7: o registro do pagamento a menor não é a cobrança do pedido; o valor aparece no total pago.
        var cobrancas = (await cobrancaRepository.ListarDoPedidoAsync(conversa.EmpresaId, pedido.Id, ct))
            .Where(c => !c.RegistraPagamentoAMenor).ToList();
        var vigente = cobrancas.FirstOrDefault(c => c.EstaPendente)
            ?? cobrancas.FirstOrDefault(c => c.Status == StatusCobrancaPedido.Paga)
            ?? cobrancas.OrderByDescending(c => c.CriadaEm).FirstOrDefault();

        // #2018: o frete é a linha de frete do checkout (PedidoItem.EhLinhaDeFrete); o resto, inclusive a linha avulsa da
        // casa, é item. Antes a linha avulsa sem produto entrava na conta do frete.
        var itens = pedido.Itens.Where(i => !i.EhLinhaDeFrete).ToList();
        var frete = pedido.Frete;

        return new PedidoConversaResult(
            pedido.Id, pedido.Status, pedido.Total.Valor, frete, pedido.CriadoEm, pedido.AgendadoParaEm,
            itens.Select(i => new ItemPedidoConversaResult(i.CardapioItemId, i.Nome, i.Quantidade, i.PrecoUnitario, i.Observacao,
                i.CardapioItemVariacaoId, i.VariacaoRotuloSnapshot)).ToList(),
            vigente is null ? null : CobrancaPedidoConversaResult.De(vigente), pedido.TotalPago,
            pedido.Pagamentos.Select(p => new PagamentoPedidoConversaResult(p.Id, p.Valor, p.Metodo, p.PagoEm)).ToList(),
            // #1474: o console barra a baixa manual antes de cancelar o link quando falta aprovar.
            pedido.RequerAprovacao && pedido.AprovadoEm is null,
            janela.Vaga is { LiberadoEm: null } ? JanelaPedidoResult.De(janela.Vaga, janela.Janela) : null,
            PlanoPagamentoEncomenda.TemSinal(pedido.AgendadoParaEm, pedido.CriadoEm),
            financeiro?.Alocado, conversa.NumeroComanda, pedido.Observacoes);
    }
}
