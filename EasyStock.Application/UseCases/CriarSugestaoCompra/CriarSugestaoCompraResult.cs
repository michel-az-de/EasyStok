namespace EasyStock.Application.UseCases.CriarSugestaoCompra;

public sealed record CriarSugestaoCompraResult(
    IReadOnlyList<PedidoCriadoResult> PedidosCriados);

public sealed record PedidoCriadoResult(
    Guid PedidoFornecedorId,
    Guid FornecedorId,
    string FornecedorNome,
    decimal? ValorEstimado,
    int ItemCount);
