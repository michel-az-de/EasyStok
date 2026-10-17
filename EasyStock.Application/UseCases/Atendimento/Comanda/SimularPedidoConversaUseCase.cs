using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.CriarPedido;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <param name="EnderecoId">Opcional: sem ele vale o endereço padrão (ou o único) do cliente, como no pedido.</param>
/// <param name="FreteCombinado">Frete combinado pela equipe, em reais; substitui a cotação da zona.</param>
public sealed record SimularPedidoConversaInput(
    Guid EmpresaId,
    Guid ConversaId,
    IReadOnlyList<ItemPedidoCheckout> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    Guid? EnderecoId = null,
    decimal? FreteCombinado = null);

/// <param name="Itens">Soma dos itens, com o preço da porção quando há.</param>
/// <param name="Frete">Frete em reais; nulo quando a entrega e o frete são combinados com a equipe.</param>
/// <param name="FreteACombinar">CEP fora de qualquer zona ou raio: o frete não tem número.</param>
/// <param name="Total">Itens mais frete; nulo com o frete a combinar.</param>
/// <param name="Encomenda">Entrega em outro dia: o servidor cobra o sinal de 50% (<c>PlanoPagamentoEncomenda</c>).</param>
/// <param name="Sinal">Metade do total com o frete; nulo fora da encomenda.</param>
/// <param name="Restante">O que fica para a entrega; nulo fora da encomenda.</param>
public sealed record SimulacaoPedidoConversaResult(
    decimal Itens,
    decimal? Frete,
    bool FreteACombinar,
    decimal? Total,
    bool Encomenda,
    decimal? Sinal,
    decimal? Restante);

/// <summary>
/// #1884 ("Quanto fica?"): antes de gerar a cobrança, a atendente vê o frete em número, o total, o sinal da
/// encomenda e o restante. Só lê: reaproveita as validações e a conta do pedido
/// (<see cref="CheckoutCoreService.SimularAsync"/>) e não cria pedido, vaga nem cobrança.
/// </summary>
public sealed class SimularPedidoConversaUseCase(
    IConversaRepository conversaRepository,
    IClienteRepository clienteRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository,
    CheckoutCoreService checkoutCore,
    TimeProvider relogio)
{
    public async Task<SimulacaoPedidoConversaResult> ExecuteAsync(SimularPedidoConversaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        if (input.Itens is null || input.Itens.Count == 0)
            throw new UseCaseValidationException("A comanda está vazia.");

        var conversa = await conversaRepository.ObterPorIdAsync(input.EmpresaId, input.ConversaId, ct)
            ?? throw new ConversaNaoEncontradaException(input.ConversaId);
        if (conversa.ClienteId is not { } clienteId)
            throw new RegraDeDominioVioladaException("Cadastre o cliente desta conversa antes de gerar o pedido.");
        var cliente = await clienteRepository.GetByIdWithDetailsAsync(input.EmpresaId, clienteId)
            ?? throw new RegraDeDominioVioladaException("Cadastre o cliente desta conversa antes de gerar o pedido.");
        var enderecoId = input.EnderecoId ?? CriarPedidoAtendimentoUseCase.EnderecoPadrao(cliente)
            ?? throw new RegraDeDominioVioladaException("Cadastre o endereço de entrega do cliente antes de gerar o pedido.");
        var endereco = cliente.Enderecos.FirstOrDefault(e => e.Id == enderecoId)
            ?? throw new RegraDeDominioVioladaException("O endereço escolhido não é deste cliente. Escolha um endereço do cadastro dele.");

        var configuracao = await configuracaoRepository.GetOrDefaultAsync(input.EmpresaId);
        var simulado = await checkoutCore.SimularAsync(new CheckoutCoreInput(
            ClienteId: cliente.Id,
            Itens: input.Itens,
            JanelaId: input.JanelaId,
            DataEntrega: input.DataEntrega,
            Cep: endereco.Cep ?? string.Empty,
            Origem: OrigemPedido.WhatsApp,
            EmpresaId: input.EmpresaId,
            Prazo: new PrazoPreparoCheckout(configuracao.TempoPreparoPadraoMinutos, configuracao.RespiroMinutos),
            Numero: endereco.Numero,
            FreteCombinado: input.FreteCombinado), ct);

        var encomenda = PlanoPagamentoEncomenda.TemSinal(simulado.AgendadoParaEm, relogio.GetUtcNow().UtcDateTime);
        if (simulado.Frete is not { } frete)
            return new SimulacaoPedidoConversaResult(simulado.Itens, null, true, null, encomenda, null, null);

        var total = simulado.Itens + frete;
        var sinal = encomenda ? PlanoPagamentoEncomenda.Sinal(total) : (decimal?)null;
        return new SimulacaoPedidoConversaResult(
            simulado.Itens, frete, false, total, encomenda, sinal, sinal is { } metade ? total - metade : null);
    }
}
