namespace EasyStock.Application.UseCases.Pedido;

/// <summary>
/// Recebe um pedido de fornecedor por completo ("recebeu tudo"): marca cada item como
/// recebido na quantidade pedida e delega ao <see cref="ProcessarRecebimentoPedidoFornecedorUseCase"/>,
/// que dá entrada no estoque por item, transiciona o status para Recebido e dispara a
/// integração financeira/notificações. Wrapper fino — não duplica a lógica de recebimento.
/// </summary>
public sealed record ReceberPedidoCompletoCommand(Guid PedidoId, Guid EmpresaId, DateTime? DataRecebimento);

public class ReceberPedidoCompletoUseCase(
    IPedidoFornecedorItemRepository itemRepository,
    ProcessarRecebimentoPedidoFornecedorUseCase processarUseCase)
{
    public async Task<ProcessarRecebimentoPedidoFornecedorResult> ExecuteAsync(
        ReceberPedidoCompletoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, nameof(cmd.PedidoId));
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var itens = (await itemRepository.GetByPedidoIdAsync(cmd.PedidoId, ct)).ToList();

        // "Recebeu tudo": novo total recebido de cada item = quantidade pedida.
        // ProcessarRecebimento calcula o delta (quantidade - jaRecebido) e dá entrada só do que falta.
        var itensRecebidos = itens.ToDictionary(i => i.Id, i => i.Quantidade);

        return await processarUseCase.ExecuteAsync(
            new ProcessarRecebimentoPedidoFornecedorCommand(
                cmd.PedidoId, cmd.EmpresaId, cmd.DataRecebimento, itensRecebidos),
            ct);
    }
}
