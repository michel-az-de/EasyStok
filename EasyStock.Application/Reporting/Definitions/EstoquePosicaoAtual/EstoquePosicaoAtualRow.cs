namespace EasyStock.Application.Reporting.Definitions.EstoquePosicaoAtual;

/// <summary>
/// Linha de dados do relatório de Posição de Estoque Atual.
/// CustoUnitario = custo da última entrada (não é CMA).
/// QtdAtual suporta valores fracionários (kg, litros).
/// </summary>
public sealed record EstoquePosicaoAtualRow(
    string   Sku,
    string   Nome,
    string   Categoria,
    string?  LojaNome,
    decimal  QtdAtual,
    decimal  CustoUnitario,
    decimal  ValorEstoque,
    string?  UltimaMovimentacao);
