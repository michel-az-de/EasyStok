using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.CalcularProducao;

public sealed record CalcularProducaoResult(
    Guid ProdutoFinalId,
    string ProdutoFinalNome,
    decimal QuantidadeDesejada,
    UnidadeMedida UnidadeDesejada,
    decimal FatorMultiplicador,
    IReadOnlyList<CalcularInsumoResult> Insumos,
    bool TudoDisponivel,
    decimal? CustoEstimadoTotal);

// Linha por insumo. Falta=null significa saldo cobre. ConversaoFalhou=true quando insumo
// esta em unidade incompativel (grupos diferentes ou Cx) — calculadora segue mesmo assim
// e marca a linha para a UI mostrar warning.
public sealed record CalcularInsumoResult(
    Guid InsumoId,
    string InsumoNome,
    decimal QuantidadeNecessaria,
    UnidadeMedida UnidadeReceita,
    decimal SaldoAtual,
    UnidadeMedida UnidadeSaldo,
    decimal? Falta,
    decimal? CustoUnitarioReferencia,
    decimal? CustoEstimadoLinha,
    bool ConversaoFalhou,
    string? Aviso);
