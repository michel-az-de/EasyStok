using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;

namespace EasyStock.Application.UseCases.RemoverPagamentoPedido;

public sealed record RemoverPagamentoPedidoCommand(
    Guid EmpresaId,
    Guid PedidoId,
    Guid PagamentoId,
    Guid? UsuarioId = null,
    string? UsuarioNome = null,
    string? Origem = "web");

public class RemoverPagamentoPedidoUseCase(
    IPedidoRepository repo,
    IUnitOfWork uow,
    ILogger<RemoverPagamentoPedidoUseCase> logger)
{
    public async Task<PedidoResult?> ExecuteAsync(RemoverPagamentoPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        UseCaseGuards.EnsureNotEmpty(cmd.PagamentoId, "PagamentoId");

        var pedido = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
        if (pedido == null) return null;

        var pag = pedido.Pagamentos.FirstOrDefault(p => p.Id == cmd.PagamentoId);
        if (pag == null) return CriarPedidoUseCase.Map(pedido);

        await repo.RemovePagamentoAsync(pag.Id);
        pedido.Pagamentos.Remove(pag);

        await repo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "pagamento_removido",
            UsuarioId = cmd.UsuarioId,
            UsuarioNome = cmd.UsuarioNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow,
            Detalhes = $"-{pag.Valor:C} ({pag.Metodo})"
        });

        await uow.CommitAsync();
        logger.LogInformation("Pedido {Id}: pagamento {Pag} removido.", pedido.Id, pag.Id);
        return CriarPedidoUseCase.Map(pedido);
    }
}
