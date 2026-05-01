using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
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
///
/// State machine explícita (transições válidas):
///   aguardando  → {preparando, cancelado}
///   preparando  → {pronto, cancelado}
///   pronto      → {entregue, cancelado}
///   entregue    → {cancelado}  (cancelamento pós-entrega devolve estoque)
///   cancelado   → ∅
///
/// Integração com estoque (PedidoEstoqueIntegrationService):
///   - Transição → "pronto" ou "entregue" (e estoque ainda não foi descontado):
///     desconta itens com ProdutoId.
///   - Transição → "cancelado" (e estoque havia sido descontado):
///     devolve itens com ProdutoId.
///   - Idempotente: usa MovimentacaoEstoque.ReferenciaDocumento = pedidoId
///     pra evitar duplicação em retries.
/// </summary>
public class AtualizarStatusPedidoUseCase(
    IPedidoRepository pedidoRepo,
    PedidoEstoqueIntegrationService estoqueIntegration,
    IUnitOfWork uow,
    ILogger<AtualizarStatusPedidoUseCase> logger)
{
    // Matriz de transições válidas (origem → conjunto de destinos permitidos).
    private static readonly Dictionary<string, HashSet<string>> Transicoes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aguardando"] = new(StringComparer.OrdinalIgnoreCase) { "preparando", "cancelado" },
        ["preparando"] = new(StringComparer.OrdinalIgnoreCase) { "pronto", "cancelado" },
        ["pronto"]     = new(StringComparer.OrdinalIgnoreCase) { "entregue", "cancelado" },
        ["entregue"]   = new(StringComparer.OrdinalIgnoreCase) { "cancelado" },
        ["cancelado"]  = new(StringComparer.OrdinalIgnoreCase)
    };

    private static readonly HashSet<string> StatusQueDescontamEstoque = new(StringComparer.OrdinalIgnoreCase)
    {
        "pronto", "entregue"
    };

    public async Task<PedidoResult?> ExecuteAsync(AtualizarStatusPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var status = (cmd.Status ?? "").Trim().ToLowerInvariant();
        if (!Transicoes.ContainsKey(status))
            throw new UseCaseValidationException($"Status inválido: {cmd.Status}");

        // CRITICAL: usar GetByIdWithDetailsAsync para carregar Itens (Include).
        // Sem isso, pedido.Itens vem vazio e a integração com estoque vira
        // no-op silencioso quando a transição deveria descontar.
        var pedido = await pedidoRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.Id);
        if (pedido == null) return null;

        var statusAtual = (pedido.Status ?? "").Trim().ToLowerInvariant();

        if (statusAtual == status)
            return CriarPedidoUseCase.Map(pedido); // idempotente

        // Validação de transição.
        if (!Transicoes.TryGetValue(statusAtual, out var destinosValidos)
            || !destinosValidos.Contains(status))
        {
            throw new UseCaseValidationException(
                $"Transição inválida: {statusAtual} → {status}.");
        }

        var statusAntigo = pedido.Status;
        var eraEstoqueDescontado = StatusQueDescontamEstoque.Contains(statusAtual);
        var seraEstoqueDescontado = StatusQueDescontamEstoque.Contains(status);

        // Integração estoque PRIMEIRO — se falhar (ex: estoque insuficiente),
        // status não é alterado e o caller recebe a exceção. Atomicidade
        // a nível de aplicação: ou ambos (status + estoque) mudam, ou nada.
        // Idempotência via DocumentoReferencia evita double-debit em retries.
        if (!eraEstoqueDescontado && seraEstoqueDescontado)
        {
            await estoqueIntegration.DescontarAsync(pedido);
        }
        else if (eraEstoqueDescontado && status == "cancelado")
        {
            await estoqueIntegration.DevolverAsync(pedido);
        }

        // Só agora muda o status (estoque já reservado/devolvido com sucesso).
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
