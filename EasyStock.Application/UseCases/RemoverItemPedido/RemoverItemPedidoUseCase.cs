using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;

namespace EasyStock.Application.UseCases.RemoverItemPedido;

public sealed record RemoverItemPedidoCommand(
    Guid EmpresaId,
    Guid PedidoId,
    Guid ItemId,
    Guid? UsuarioId = null,
    string? UsuarioNome = null,
    string? Origem = "web");

public class RemoverItemPedidoUseCase(
    IPedidoRepository repo,
    IUnitOfWork uow,
    ILogger<RemoverItemPedidoUseCase> logger)
{
    public async Task<PedidoResult?> ExecuteAsync(RemoverItemPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        UseCaseGuards.EnsureNotEmpty(cmd.ItemId, "ItemId");

        var pedido = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
        if (pedido == null) return null;
        if (pedido.EstaFinalizado)
            throw new UseCaseValidationException("Não é permitido alterar itens de pedido finalizado.");

        var item = pedido.Itens.FirstOrDefault(i => i.Id == cmd.ItemId);
        if (item == null) return CriarPedidoUseCase.Map(pedido);

        await repo.RemoveItemAsync(cmd.EmpresaId, item.Id);
        pedido.Itens.Remove(item);
        pedido.RecalcularTotal();

        await repo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "item_removed",
            UsuarioId = cmd.UsuarioId,
            UsuarioNome = cmd.UsuarioNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow,
            Detalhes = $"-{item.Quantidade} {item.Nome} ({item.Subtotal:C})"
        });

        await repo.UpdateAsync(pedido);
        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id}: item {Item} removido, novo total {Total}.",
            pedido.Id, item.Nome, pedido.Total);
        return CriarPedidoUseCase.Map(pedido);
    }
}
