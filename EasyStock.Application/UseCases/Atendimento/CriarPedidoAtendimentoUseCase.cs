using EasyStock.Application.Ports.Output.Persistence.Atendimento;
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
/// <see cref="PedidoReservado"/> devolvido aqui.
/// </summary>
public sealed class CriarPedidoAtendimentoUseCase(
    CheckoutCoreService checkoutCore,
    IConversaRepository conversaRepository,
    IClienteRepository clienteRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<PedidoReservado> ExecuteAsync(
        CriarPedidoAtendimentoInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var conversa = await conversaRepository.ObterPorIdAsync(input.EmpresaId, input.ConversaId, ct)
            ?? throw new RegraDeDominioVioladaException($"Conversa {input.ConversaId} não encontrada.");

        var cliente = await clienteRepository.GetByIdWithDetailsAsync(input.EmpresaId, input.ClienteId)
            ?? throw new RegraDeDominioVioladaException($"Cliente {input.ClienteId} não encontrado.");

        var endereco = cliente.Enderecos.FirstOrDefault(e => e.Id == input.EnderecoId)
            ?? throw new RegraDeDominioVioladaException($"Endereço {input.EnderecoId} não pertence ao cliente.");

        var reservado = await checkoutCore.CriarPedidoComReservaAsync(
            new CheckoutCoreInput(
                ClienteId: cliente.Id,
                Itens: input.Itens,
                JanelaId: input.JanelaId,
                DataEntrega: input.DataEntrega,
                Cep: endereco.Cep ?? string.Empty,
                Origem: OrigemPedido.WhatsApp,
                EmpresaId: input.EmpresaId,
                Observacoes: input.Observacoes),
            ct);

        conversa.DefinirPedidoEmAndamento(reservado.Pedido.Id);
        await unitOfWork.CommitAsync();

        return reservado;
    }
}
