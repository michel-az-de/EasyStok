using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Exceptions.Storefront;

using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <param name="EnderecoId">Opcional: sem ele vale o endereço padrão (ou o único) do cliente.</param>
/// <param name="EnviarAoCliente">False cria a cobrança para copiar, sem mensagem ou falsa falha de envio (#1847).</param>
/// <param name="Forma"><c>online</c> (padrão, link do Mercado Pago) ou <c>na_entrega</c>.</param>
/// <param name="UsuarioId">Quem gerou pelo console (#1991-7): o resumo ao cliente sai em nome dele, não como automática.</param>
public sealed record GerarPedidoConversaInput(
    Guid EmpresaId,
    Guid ConversaId,
    IReadOnlyList<ItemPedidoCheckout> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    Guid? EnderecoId = null,
    string? Forma = null,
    string? Observacoes = null,
    string? UsuarioNome = null,
    bool EnviarAoCliente = true,
    Guid? UsuarioId = null);

/// <param name="Cobranca">Cobrança da forma escolhida. Mercado Pago fora: <see cref="MercadoPagoIndisponivelException"/> com o pedido já desfeito (#1301).</param>
/// <param name="EnviadoAoCliente">O resumo saiu pelo canal da conversa (falso fora da janela ou com o canal fora).</param>
public sealed record PedidoConversaGeradoResult(
    Guid PedidoId,
    decimal Total,
    string Forma,
    CobrancaPedidoResult? Cobranca,
    bool EnviadoAoCliente);

/// <summary>
/// F03 do console: a operadora fecha a comanda da conversa. O pedido nasce pelo caminho da conversa
/// (S10, <see cref="CriarPedidoAtendimentoUseCase"/>), a cobrança pela S11 na forma escolhida e o resumo,
/// com o link quando houver, sai ao cliente pela porta do canal (<see cref="AvisoCobrancaConversa"/>).
///
/// <para>
/// Um pedido por vez: com pedido em andamento ainda não finalizado na conversa, recusa (clique duplo
/// não cria dois pedidos nem ocupa duas vagas). A regra vive no núcleo, com a conversa travada (#1238).
/// </para>
/// </summary>
public sealed class GerarPedidoConversaUseCase(
    IConversaRepository conversaRepository,
    IClienteRepository clienteRepository,
    CriarPedidoAtendimentoUseCase criarPedido,
    GerarCobrancaPedidoUseCase gerarCobranca,
    TrocarFormaPagamentoPedidoUseCase trocarForma,
    AvisoCobrancaConversa aviso,
    TimeProvider relogio)
{
    public const string CabecalhoResumo = "Resumo do seu pedido:";

    public async Task<PedidoConversaGeradoResult> ExecuteAsync(GerarPedidoConversaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        var forma = string.IsNullOrWhiteSpace(input.Forma)
            ? TrocarFormaPagamentoPedidoUseCase.FormaOnline
            : input.Forma.Trim().ToLowerInvariant();
        if (forma is not (TrocarFormaPagamentoPedidoUseCase.FormaOnline or TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega))
            throw new UseCaseValidationException("Forma de pagamento deve ser 'online' ou 'na_entrega'.");
        if (input.Itens is null || input.Itens.Count == 0)
            throw new UseCaseValidationException("A comanda está vazia.");

        var conversa = await conversaRepository.ObterPorIdAsync(input.EmpresaId, input.ConversaId, ct)
            ?? throw new ConversaNaoEncontradaException(input.ConversaId);
        if (!conversa.EstaAberta)
            throw new RegraDeDominioVioladaException("A conversa está encerrada.");

        if (conversa.ClienteId is not { } clienteId)
            throw new RegraDeDominioVioladaException("Cadastre o cliente desta conversa antes de gerar o pedido.");
        var cliente = await clienteRepository.GetByIdWithDetailsAsync(input.EmpresaId, clienteId)
            ?? throw new RegraDeDominioVioladaException("Cadastre o cliente desta conversa antes de gerar o pedido.");
        var enderecoId = input.EnderecoId ?? CriarPedidoAtendimentoUseCase.EnderecoPadrao(cliente)
            ?? throw new RegraDeDominioVioladaException("Cadastre o endereço de entrega do cliente antes de gerar o pedido.");

        var reservado = await criarPedido.ExecuteAsync(new CriarPedidoAtendimentoInput(
            input.EmpresaId, conversa.Id, clienteId, input.Itens, input.JanelaId, input.DataEntrega, enderecoId,
            input.Observacoes), ct);

        var cobranca = await CobrarAsync(reservado, conversa.Id, forma, input.UsuarioNome, ct);
        var texto = TextoAoCliente(reservado, input.DataEntrega, forma, cobranca);
        var enviado = input.EnviarAoCliente && await aviso.EnviarAsync(input.EmpresaId, conversa.Id, texto,
            relogio.GetUtcNow().UtcDateTime, ct, input.UsuarioId);

        return new PedidoConversaGeradoResult(reservado.Pedido.Id, reservado.Total, forma, cobranca, enviado);
    }

    /// <summary>Mercado Pago fora propaga: a cobrança já desfez o pedido e a vaga, e a operadora tenta de novo (#1301).</summary>
    private async Task<CobrancaPedidoResult?> CobrarAsync(
        PedidoReservado reservado, Guid conversaId, string forma, string? usuarioNome, CancellationToken ct)
    {
        if (forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega)
            return (await trocarForma.ExecuteAsync(new TrocarFormaPagamentoPedidoInput(
                reservado.Pedido.EmpresaId, reservado.Pedido.Id, forma, UsuarioNome: usuarioNome ?? "console"), ct)).Cobranca;
        return await gerarCobranca.ExecuteAsync(reservado, conversaId, ct);
    }

    private static string TextoAoCliente(PedidoReservado reservado, DateOnly dataEntrega, string forma, CobrancaPedidoResult? cobranca)
    {
        var resumo = ResumoPedidoConversa.Texto(reservado, dataEntrega, forma, CabecalhoResumo);
        if (cobranca is { Provedor: CobrancaPedido.ProvedorNaEntrega }
            && PlanoPagamentoEncomenda.TemSinal(reservado.Pedido.AgendadoParaEm, reservado.Pedido.CriadoEm))
            return ResumoPedidoConversa.Limitar($"{resumo}\n\nSinal a receber: {PlanoPagamentoEncomenda.Sinal(reservado.Total).ToString("C", Cultura.PtBr)} por Pix, dinheiro ou maquininha. Restante na entrega.");
        if (forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega
            && !PlanoPagamentoEncomenda.TemSinal(reservado.Pedido.AgendadoParaEm, reservado.Pedido.CriadoEm)) return resumo;
        var pagamento = cobranca?.LinkPagamento is { } link
            ? $"Pague por este link, válido por 30 minutos: {link}"
            : "O link de pagamento chega em instantes.";
        return ResumoPedidoConversa.Limitar($"{resumo}\n\n{pagamento}");
    }
}
