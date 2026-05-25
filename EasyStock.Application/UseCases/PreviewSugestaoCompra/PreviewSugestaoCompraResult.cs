using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.PreviewSugestaoCompra;

public sealed record PreviewSugestaoCompraResult(
    IReadOnlyList<SugestaoPorFornecedorResult> PorFornecedor,
    decimal? TotalEstimado);

// FornecedorId null = bucket "Sem fornecedor preferido" — UI mostra lista de fornecedores
// ativos para o operador escolher antes de chamar criar-compra (G6).
public sealed record SugestaoPorFornecedorResult(
    Guid? FornecedorId,
    string FornecedorNome,
    IReadOnlyList<SugestaoLinhaResult> Linhas,
    decimal? SubtotalEstimado);

public sealed record SugestaoLinhaResult(
    Guid InsumoId,
    string InsumoNome,
    decimal Quantidade,
    UnidadeMedida Unidade,
    decimal? CustoUnitarioReferencia,
    decimal? Subtotal);
