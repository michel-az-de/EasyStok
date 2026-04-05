using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.Fornecedor;

public sealed record FornecedorPedidoHistoricoItemResult(
    Guid PedidoId,
    DateTime DataPedido,
    DateTime? PrevisaoEntrega,
    DateTime? DataRecebimento,
    decimal? ValorEstimado,
    StatusPedidoFornecedor Status,
    string? Canal,
    string? Tracking,
    string? Observacoes);
