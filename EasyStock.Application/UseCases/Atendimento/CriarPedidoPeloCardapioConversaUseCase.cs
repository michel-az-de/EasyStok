using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento;

/// <param name="Token">O <c>?c=</c> do link enviado na conversa.</param>
/// <param name="EnderecoId">Opcional: sem ele vale o endereço padrão (ou o único) do cliente.</param>
/// <param name="Forma"><c>online</c> (padrão, link do Mercado Pago) ou <c>na_entrega</c>.</param>
public sealed record CriarPedidoPeloCardapioConversaInput(
    string? Token,
    IReadOnlyList<ItemPedidoCheckout> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    Guid? EnderecoId = null,
    string? Forma = null,
    string? Observacoes = null);

/// <param name="LinkPagamento">Nulo na entrega. Mercado Pago fora: <see cref="MercadoPagoIndisponivelException"/> com o pedido desfeito e o link do cardápio devolvido (#1301).</param>
public sealed record PedidoPeloCardapioConversaResult(
    Guid PedidoId,
    decimal Total,
    string Forma,
    string? LinkPagamento,
    DateTime? PagamentoExpiraEm);

/// <summary>
/// Pedido que o cliente monta no cardápio do site a partir do link da conversa (S48). O token amarra o
/// carrinho à <see cref="Conversa"/>: o pedido nasce pelo caminho da conversa (S10,
/// <see cref="CriarPedidoAtendimentoUseCase"/>, que grava <c>Conversa.PedidoEmAndamentoId</c>), o resumo
/// fica como <c>Mensagem(Sistema)</c> sem id externo (nota para a operadora e para o agente, não sai
/// para o cliente) e a cobrança segue pela S11 na forma escolhida. Depois do commit, o console é avisado
/// pelo SSE (S18) com <see cref="EventosOperacao.ConversaPedidoPelaPagina"/> e
/// <see cref="EventosOperacao.ConversaMensagemRecebida"/> (a mensagem de resumo).
///
/// <para>
/// A requisição é anônima: o link encontrado pelo hash do token liga o tenant (RLS). O uso é marcado
/// de forma atômica antes de criar o pedido, então dois envios simultâneos não criam dois pedidos; se o
/// carrinho for recusado (janela, CEP, item), o uso é devolvido para o cliente corrigir e reenviar.
/// </para>
/// </summary>
public sealed class CriarPedidoPeloCardapioConversaUseCase(
    LinkCardapioConversaService linkService,
    ITenantContextAccessor tenantContext,
    IConversaRepository conversaRepository,
    IClienteRepository clienteRepository,
    CriarPedidoAtendimentoUseCase criarPedido,
    GerarCobrancaPedidoUseCase gerarCobranca,
    TrocarFormaPagamentoPedidoUseCase trocarForma,
    IOperacaoEventPublisher eventPublisher,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<CriarPedidoPeloCardapioConversaUseCase> logger)
{
    private const string ClienteNaoIdentificado =
        "Não identificamos o cliente desta conversa. Conclua o pedido pela conversa.";

    public async Task<PedidoPeloCardapioConversaResult> ExecuteAsync(
        CriarPedidoPeloCardapioConversaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var forma = string.IsNullOrWhiteSpace(input.Forma)
            ? TrocarFormaPagamentoPedidoUseCase.FormaOnline
            : input.Forma.Trim().ToLowerInvariant();
        if (forma is not (TrocarFormaPagamentoPedidoUseCase.FormaOnline or TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega))
            throw new UseCaseValidationException("Forma de pagamento deve ser 'online' ou 'na_entrega'.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        var link = await linkService.ValidarAsync(input.Token, agora, ct);
        tenantContext.SetCurrentTenant(link.EmpresaId);

        var conversa = await conversaRepository.ObterPorIdAsync(link.EmpresaId, link.ConversaId, ct);
        if (conversa is null || !conversa.EstaAberta)
            throw new LinkCardapioConversaIndisponivelException();
        if (conversa.ClienteId is not { } clienteId)
            throw new RegraDeDominioVioladaException(ClienteNaoIdentificado);

        var cliente = await clienteRepository.GetByIdWithDetailsAsync(link.EmpresaId, clienteId)
            ?? throw new RegraDeDominioVioladaException(ClienteNaoIdentificado);
        var enderecoId = input.EnderecoId ?? CriarPedidoAtendimentoUseCase.EnderecoPadrao(cliente)
            ?? throw new RegraDeDominioVioladaException("Informe o endereço de entrega na conversa antes de enviar o pedido.");

        await linkService.ConsumirAsync(link, agora, ct);
        PedidoReservado reservado;
        try
        {
            reservado = await criarPedido.ExecuteAsync(new CriarPedidoAtendimentoInput(
                link.EmpresaId, conversa.Id, clienteId, input.Itens, input.JanelaId, input.DataEntrega, enderecoId,
                input.Observacoes), ct);
        }
        catch
        {
            await linkService.LiberarAsync(link, CancellationToken.None);
            throw;
        }

        conversa.RegistrarSaida(agora);
        var resumo = Mensagem.Saida(link.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Texto,
            ResumoPedidoConversa.Texto(reservado, input.DataEntrega, forma,
                "Pedido montado pelo cliente no cardápio do site:"));
        await conversaRepository.AddMensagemAsync(resumo, ct);
        await unitOfWork.CommitAsync();
        await AvisarConsoleAsync(reservado, resumo, ct);

        CobrancaPedidoResult? cobranca;
        try
        {
            cobranca = await CobrarAsync(reservado, conversa.Id, forma, ct);
        }
        catch (MercadoPagoIndisponivelException ex)
        {
            // #1301: a cobrança desfez o pedido e a vaga; o cliente reenvia o carrinho pelo mesmo link.
            logger.LogWarning(ex, "Cardapio da conversa: pedido {PedidoId} desfeito sem link de pagamento.", reservado.Pedido.Id);
            await linkService.LiberarAsync(link, CancellationToken.None);
            throw;
        }
        return new PedidoPeloCardapioConversaResult(
            reservado.Pedido.Id, reservado.Total, forma, cobranca?.LinkPagamento, cobranca?.ExpiraEm);
    }

    /// <summary>Evento de UI depois do commit: se o aviso falhar, o pedido já gravado segue (a inbox vê ao recarregar).</summary>
    private async Task AvisarConsoleAsync(PedidoReservado reservado, Mensagem resumo, CancellationToken ct)
    {
        var pedido = reservado.Pedido;
        try
        {
            await eventPublisher.PublicarAsync(EventosOperacao.ConversaPedidoPelaPagina, pedido.EmpresaId,
                new ConversaPedidoPelaPaginaOperacao(resumo.ConversaId, pedido.Id,
                    pedido.Id.ToString("N")[..8].ToUpperInvariant(), reservado.Total), ct);
            await eventPublisher.PublicarAsync(EventosOperacao.ConversaMensagemRecebida, pedido.EmpresaId,
                new { conversaId = resumo.ConversaId, mensagemId = resumo.Id }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cardapio da conversa: pedido {PedidoId} gravado, aviso ao console falhou.", pedido.Id);
        }
    }

    private async Task<CobrancaPedidoResult?> CobrarAsync(
        PedidoReservado reservado, Guid conversaId, string forma, CancellationToken ct)
    {
        if (forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega)
            return (await trocarForma.ExecuteAsync(new TrocarFormaPagamentoPedidoInput(
                reservado.Pedido.EmpresaId, reservado.Pedido.Id, forma, UsuarioNome: "cardápio da conversa"), ct)).Cobranca;
        return await gerarCobranca.ExecuteAsync(reservado, conversaId, ct);
    }
}
