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

        // Remocao rastreada: o pedido veio tracked de GetByIdWithDetailsAsync e a FK
        // PedidoItem->Pedido e required+Cascade, entao remover da colecao marca o item
        // como Deleted e o DELETE sai no MESMO SaveChanges do RecalcularTotal + evento
        // (atomico). Antes havia um ExecuteDeleteAsync imediato (fora do UoW) que, se o
        // commit final falhasse, deixava o item apagado com o Total antigo inconsistente. #768
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
            Detalhes = $"-{item.Quantidade} {item.Nome} ({item.Subtotal.ToString("C", Cultura.PtBr)})"
        });

        await repo.UpdateAsync(pedido);
        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id}: item {Item} removido, novo total {Total}.",
            pedido.Id, item.Nome, pedido.Total);
        return CriarPedidoUseCase.Map(pedido);
    }
}
