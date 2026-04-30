using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AtualizarStatusPedido;

public sealed record AtualizarStatusPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    [property: Required][property: MaxLength(20)] string Status,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "web");

/// <summary>
/// Atualiza o status do pedido (aguardando → preparando → pronto → entregue).
/// Registra evento de mudança e mantém AlteradoEm/EntreguEm consistentes.
/// </summary>
public class AtualizarStatusPedidoUseCase(
    IPedidoRepository pedidoRepo,
    IUnitOfWork uow,
    ILogger<AtualizarStatusPedidoUseCase> logger)
{
    private static readonly HashSet<string> StatusValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "aguardando", "preparando", "pronto", "entregue", "cancelado"
    };

    public async Task<PedidoResult?> ExecuteAsync(AtualizarStatusPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var status = (cmd.Status ?? "").Trim().ToLowerInvariant();
        if (!StatusValidos.Contains(status))
            throw new UseCaseValidationException($"Status inválido: {cmd.Status}");

        var pedido = await pedidoRepo.GetByIdAsync(cmd.EmpresaId, cmd.Id);
        if (pedido == null) return null;

        if (pedido.EstaFinalizado)
            throw new UseCaseValidationException("Pedido finalizado não pode mudar de status. Use reabrir/duplicar.");

        if (pedido.Status == status)
            return CriarPedidoUseCase.Map(pedido); // idempotente

        var statusAntigo = pedido.Status;

        if (status == "entregue") pedido.MarcarEntregue();
        else if (status == "cancelado") pedido.Cancelar();
        else { pedido.Status = status; pedido.AlteradoEm = DateTime.UtcNow; }

        await pedidoRepo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "status_changed",
            StatusAntigo = statusAntigo,
            StatusNovo = status,
            UsuarioId = cmd.UsuarioId,
            UsuarioNome = cmd.UsuarioNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow
        });

        await pedidoRepo.UpdateAsync(pedido);
        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id} status {Antigo} → {Novo}.", pedido.Id, statusAntigo, status);
        return CriarPedidoUseCase.Map(pedido);
    }
}
