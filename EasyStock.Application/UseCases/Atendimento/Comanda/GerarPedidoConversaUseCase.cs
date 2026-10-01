using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <param name="EnderecoId">Opcional: sem ele vale o endereço padrão (ou o único) do cliente.</param>
/// <param name="Forma"><c>online</c> (padrão, link do Mercado Pago) ou <c>na_entrega</c>.</param>
public sealed record GerarPedidoConversaInput(
    Guid EmpresaId,
    Guid ConversaId,
    IReadOnlyList<ItemPedidoCheckout> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    Guid? EnderecoId = null,
    string? Forma = null,
    string? Observacoes = null,
    string? UsuarioNome = null);

/// <param name="Cobranca">Nula quando o Mercado Pago não respondeu: o link sai pela reemissão (job ou operadora).</param>
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
    TimeProvider relogio,
    ILogger<GerarPedidoConversaUseCase> logger)
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
        var enviado = await aviso.EnviarAsync(input.EmpresaId, conversa.Id, texto, relogio.GetUtcNow().UtcDateTime, ct);

        return new PedidoConversaGeradoResult(reservado.Pedido.Id, reservado.Total, forma, cobranca, enviado);
    }

    private async Task<CobrancaPedidoResult?> CobrarAsync(
        PedidoReservado reservado, Guid conversaId, string forma, string? usuarioNome, CancellationToken ct)
    {
        try
        {
            if (forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega)
                return (await trocarForma.ExecuteAsync(new TrocarFormaPagamentoPedidoInput(
                    reservado.Pedido.EmpresaId, reservado.Pedido.Id, forma, UsuarioNome: usuarioNome ?? "console"), ct)).Cobranca;
            return await gerarCobranca.ExecuteAsync(reservado, conversaId, ct);
        }
        catch (MercadoPagoIndisponivelException ex)
        {
            // Pedido criado e vaga reservada; o link sai pela reemissão (operadora ou job), como na criar_pedido.
            logger.LogWarning(ex, "Console: pedido {PedidoId} criado sem link de pagamento.", reservado.Pedido.Id);
            return null;
        }
    }

    private static string TextoAoCliente(PedidoReservado reservado, DateOnly dataEntrega, string forma, CobrancaPedidoResult? cobranca)
    {
        var resumo = ResumoPedidoConversa.Texto(reservado, dataEntrega, forma, CabecalhoResumo);
        if (forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega) return resumo;
        var pagamento = cobranca?.LinkPagamento is { } link
            ? $"Pague por este link, válido por 30 minutos: {link}"
            : "O link de pagamento chega em instantes.";
        return ResumoPedidoConversa.Limitar($"{resumo}\n\n{pagamento}");
    }
}
