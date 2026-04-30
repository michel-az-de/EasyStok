using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.CancelarPedido;

public sealed record CancelarPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    string? Motivo = null,
    [property: MaxLength(20)] string? Origem = "web");

public class CancelarPedidoUseCase(
    IPedidoRepository pedidoRepo,
    IUnitOfWork uow,
    ILogger<CancelarPedidoUseCase> logger)
{
    public async Task<PedidoResult?> ExecuteAsync(CancelarPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var pedido = await pedidoRepo.GetByIdAsync(cmd.EmpresaId, cmd.Id);
        if (pedido == null) return null;
        if (pedido.Status == "cancelado") return CriarPedidoUseCase.Map(pedido);

        var statusAntigo = pedido.Status;
        pedido.Cancelar();

        await pedidoRepo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "cancelado",
            StatusAntigo = statusAntigo,
            StatusNovo = "cancelado",
            Detalhes = cmd.Motivo,
            UsuarioId = cmd.UsuarioId,
            UsuarioNome = cmd.UsuarioNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow
        });

        await pedidoRepo.UpdateAsync(pedido);
        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id} cancelado (motivo={Motivo}).", pedido.Id, cmd.Motivo ?? "—");
        return CriarPedidoUseCase.Map(pedido);
    }
}
