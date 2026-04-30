using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.RegistrarPagamentoPedido;

public sealed record RegistrarPagamentoPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid PedidoId,
    [property: Required][property: MaxLength(20)] string Metodo,
    decimal Valor,
    [property: MaxLength(120)] string? Referencia = null,
    string? Observacao = null,
    Guid? RegistradoPorUserId = null,
    [property: MaxLength(120)] string? RegistradoPorNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class RegistrarPagamentoPedidoUseCase(
    IPedidoRepository repo,
    IUnitOfWork uow,
    ILogger<RegistrarPagamentoPedidoUseCase> logger)
{
    private static readonly HashSet<string> MetodosValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "pix", "dinheiro", "credito", "debito", "transferencia", "outro"
    };

    public async Task<PedidoResult?> ExecuteAsync(RegistrarPagamentoPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        if (cmd.Valor <= 0)
            throw new UseCaseValidationException("Valor do pagamento deve ser maior que zero.");

        var metodo = (cmd.Metodo ?? "").Trim().ToLowerInvariant();
        if (!MetodosValidos.Contains(metodo))
            throw new UseCaseValidationException($"Método inválido: {cmd.Metodo}");

        var pedido = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
        if (pedido == null) return null;

        var pag = new PedidoPagamento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Metodo = metodo,
            Valor = cmd.Valor,
            Referencia = cmd.Referencia,
            Observacao = cmd.Observacao,
            PagoEm = DateTime.UtcNow,
            RegistradoPorUserId = cmd.RegistradoPorUserId,
            RegistradoPorNome = cmd.RegistradoPorNome
        };

        await repo.AddPagamentoAsync(pag);
        pedido.Pagamentos.Add(pag);

        await repo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "pagamento",
            UsuarioId = cmd.RegistradoPorUserId,
            UsuarioNome = cmd.RegistradoPorNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow,
            Detalhes = $"+{pag.Valor:C} via {metodo}"
        });

        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id}: pagamento {Valor} {Metodo} (TotalPago={TotalPago}/{Total}).",
            pedido.Id, pag.Valor, metodo, pedido.TotalPago, pedido.Total);
        return CriarPedidoUseCase.Map(pedido);
    }
}
