using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.CriarPedido;

namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>
/// Pedido fechado na conversa (S10): endereço do cliente, janela e data vêm do agente.
/// </summary>
public sealed record CriarPedidoAtendimentoInput(
    Guid EmpresaId,
    Guid ConversaId,
    Guid ClienteId,
    IReadOnlyList<ItemPedidoCheckout> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    Guid EnderecoId,
    string? Observacoes = null);

/// <summary>
/// Cria o pedido da conversa pelo mesmo núcleo do site (<see cref="CheckoutCoreService"/>): itens com
/// snapshot do cardápio e observação por item, frete pelo CEP do endereço escolhido, vaga ocupada e
/// pedido em <c>AguardandoPagamento</c> com <c>Origem = "whatsapp"</c>. Grava
/// <c>Conversa.PedidoEmAndamentoId</c>. A cobrança fica com a S11, que recebe o
/// <see cref="PedidoReservado"/> devolvido aqui. O núcleo revalida o prazo mínimo (S16, RN-21) com o preparo
/// padrão e o respiro de <c>ConfiguracaoAtendimento</c>.
/// </summary>
public sealed class CriarPedidoAtendimentoUseCase(
    CheckoutCoreService checkoutCore,
    IConversaRepository conversaRepository,
    IClienteRepository clienteRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository,
    IUnitOfWork unitOfWork)
{
    /// <summary>Motivo gravado em <c>Pedido.MotivoRequerAprovacao</c> quando a dona liberou o lead fora de área (S14).</summary>
    public const string MotivoForaDeArea = "fora_de_area";

    public async Task<PedidoReservado> ExecuteAsync(
        CriarPedidoAtendimentoInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var conversa = await conversaRepository.ObterPorIdAsync(input.EmpresaId, input.ConversaId, ct)
            ?? throw new RegraDeDominioVioladaException($"Conversa {input.ConversaId} não encontrada.");

        if (conversa.ClienteId != input.ClienteId)
            throw new RegraDeDominioVioladaException(
                $"Cliente {input.ClienteId} não é o cliente vinculado à conversa {input.ConversaId}.");

        var cliente = await clienteRepository.GetByIdWithDetailsAsync(input.EmpresaId, input.ClienteId)
            ?? throw new RegraDeDominioVioladaException($"Cliente {input.ClienteId} não encontrado.");

        // S24: bloqueio vale em todos os canais; nada de vaga ocupada nem pedido.
        if (cliente.Bloqueado)
            throw new ClienteBloqueadoException(cliente.Id);

        var endereco = cliente.Enderecos.FirstOrDefault(e => e.Id == input.EnderecoId)
            ?? throw new RegraDeDominioVioladaException($"Endereço {input.EnderecoId} não pertence ao cliente.");

        var configuracao = await configuracaoRepository.GetOrDefaultAsync(input.EmpresaId);

        var reservado = await checkoutCore.CriarPedidoComReservaAsync(
            new CheckoutCoreInput(
                ClienteId: cliente.Id,
                Itens: input.Itens,
                JanelaId: input.JanelaId,
                DataEntrega: input.DataEntrega,
                Cep: endereco.Cep ?? string.Empty,
                Origem: OrigemPedido.WhatsApp,
                EmpresaId: input.EmpresaId,
                Observacoes: input.Observacoes,
                Prazo: new PrazoPreparoCheckout(configuracao.TempoPreparoPadraoMinutos, configuracao.RespiroMinutos)),
            ct);

        // S14: a dona liberou o lead fora de área; pago, o pedido espera por ela (S12/S13), não vai para a cozinha.
        if (ContextoConversaJson.Ler<bool>(conversa, ContextoConversaJson.ForaDeAreaLiberado))
            reservado.Pedido.MarcarRequerAprovacao(MotivoForaDeArea);

        conversa.DefinirPedidoEmAndamento(reservado.Pedido.Id);
        await unitOfWork.CommitAsync();

        return reservado;
    }

    /// <summary>Endereço padrão do cliente ou, sem padrão, o único cadastrado; senão nulo (é preciso perguntar).</summary>
    public static Guid? EnderecoPadrao(EasyStock.Domain.Entities.Cliente cliente)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        var padrao = cliente.Enderecos.FirstOrDefault(e => e.Padrao);
        if (padrao is not null) return padrao.Id;
        return cliente.Enderecos.Count == 1 ? cliente.Enderecos.First().Id : null;
    }
}
