using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.PedidoFornecedor;

public sealed record ItemPedidoFornecedorResult(
    Guid Id,
    Guid? ProdutoId,
    Guid? ProdutoVariacaoId,
    string Descricao,
    decimal Quantidade,
    decimal? CustoUnitario);

public sealed record PedidoFornecedorResult(
    Guid Id,
    Guid EmpresaId,
    Guid FornecedorId,
    string? FornecedorNome,
    DateTime DataPedido,
    DateTime? PrevisaoEntrega,
    DateTime? DataRecebimento,
    decimal? ValorEstimado,
    StatusPedidoFornecedor Status,
    string? Canal,
    string? Tracking,
    string? Observacoes,
    IReadOnlyCollection<ItemPedidoFornecedorResult> Itens);
