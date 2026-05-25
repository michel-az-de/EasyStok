namespace EasyStock.Application.UseCases.AdicionarItemPedidoFornecedor;

public sealed record AdicionarItemPedidoFornecedorCommand(
    Guid EmpresaId,
    Guid PedidoFornecedorId,
    Guid? ProdutoId,
    string Nome,
    string? Unidade,
    decimal Quantidade,
    decimal CustoUnitario,
    string? Observacao);
