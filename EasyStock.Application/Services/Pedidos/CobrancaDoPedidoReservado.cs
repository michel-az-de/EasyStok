using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Pedidos.Cobranca;

namespace EasyStock.Application.Services.Pedidos;

/// <summary>Quem fecha o pedido da conversa: decide o que acontece com o sinal da encomenda paga "na entrega".</summary>
public enum QuemCobra
{
    /// <summary>A atendente, pelo console: há uma pessoa para combinar o sinal com o cliente.</summary>
    Atendente,

    /// <summary>O cliente sozinho, pelo link do cardápio: ninguém combina nada, o sinal sai como link.</summary>
    ClientePeloLink,
}

/// <summary>
/// #2018 (PEDIDO-08, comanda-14): a regra de "online x na entrega" e do sinal da encomenda, escrita uma vez para o pedido
/// que o console fecha (<c>GerarPedidoConversaUseCase</c>) e para o que o cliente fecha pelo link do cardápio
/// (<c>CriarPedidoPeloCardapioConversaUseCase</c>). Antes cada um tinha a sua <c>CobrarAsync</c>, e elas diferiam em um
/// ponto que nenhum texto explicava.
///
/// <para>
/// A regra: pagar "online" gera o link do Mercado Pago (o sinal, na encomenda). Pagar "na entrega" troca a forma do pedido e
/// não gera link. A encomenda (entrega em outro dia, <see cref="PlanoPagamentoEncomenda.TemSinal"/>) exige o sinal antes da
/// produção em qualquer forma: com a <see cref="QuemCobra.Atendente"/> a equipe combina o sinal (o resumo diz "Combine com a
/// equipe como pagar o sinal"); com o <see cref="QuemCobra.ClientePeloLink"/> não há ninguém para combinar, então o sinal
/// sai como link e o restante fica para a entrega.
/// </para>
/// </summary>
public static class CobrancaDoPedidoReservado
{
    /// <summary>Normaliza a forma pedida (vazia vira <c>online</c>) e recusa o que não é <c>online</c> nem <c>na_entrega</c>.</summary>
    public static string NormalizarForma(string? forma)
    {
        var normalizada = string.IsNullOrWhiteSpace(forma)
            ? TrocarFormaPagamentoPedidoUseCase.FormaOnline
            : forma.Trim().ToLowerInvariant();
        if (normalizada is not (TrocarFormaPagamentoPedidoUseCase.FormaOnline or TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega))
            throw new UseCaseValidationException("Forma de pagamento deve ser 'online' ou 'na_entrega'.");
        return normalizada;
    }

    /// <summary>
    /// A forma que a cobrança segue de fato: "na entrega" de encomenda feita pelo cliente sozinho vira <c>online</c> (link do
    /// sinal); nos demais casos vale a forma escolhida.
    /// </summary>
    public static string FormaEfetiva(string forma, Pedido pedido, QuemCobra quem)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        return forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega
            && quem == QuemCobra.ClientePeloLink
            && PlanoPagamentoEncomenda.TemSinal(pedido.AgendadoParaEm, pedido.CriadoEm)
                ? TrocarFormaPagamentoPedidoUseCase.FormaOnline
                : forma;
    }

    /// <summary>
    /// Cobra o pedido recém-reservado pela <see cref="FormaEfetiva"/>. Mercado Pago fora propaga: a cobrança já desfez o pedido
    /// e a vaga, e quem chamou tenta de novo (#1301).
    /// </summary>
    public static async Task<CobrancaPedidoResult?> CobrarAsync(
        GerarCobrancaPedidoUseCase gerarCobranca, TrocarFormaPagamentoPedidoUseCase trocarForma,
        PedidoReservado reservado, Guid conversaId, string forma, QuemCobra quem, string usuarioNome, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reservado);
        if (FormaEfetiva(forma, reservado.Pedido, quem) == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega)
            return (await trocarForma.ExecuteAsync(new TrocarFormaPagamentoPedidoInput(
                reservado.Pedido.EmpresaId, reservado.Pedido.Id, forma, UsuarioNome: usuarioNome), ct)).Cobranca;
        return await gerarCobranca.ExecuteAsync(reservado, conversaId, ct);
    }
}
