using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.CalcularProducao;

/// <summary>
/// Resultado de calculo de cesta. Itens = 1 entrada por produto-final do pedido (com Status
/// indicando se calculou, se nao tem receita, ou se houve erro). Consolidado = mesmos insumos
/// agregados entre todos os produtos (cozinha pensa por produto, compras pensa por insumo total).
/// </summary>
public sealed record CalcularCestaProducaoResult(
    IReadOnlyList<ItemCestaResult> Itens,
    IReadOnlyList<InsumoConsolidadoResult> Consolidado,
    bool TudoDisponivel,
    decimal? CustoEstimadoTotal);

public enum ItemCestaStatus
{
    /// <summary>Calculado com sucesso. <see cref="ItemCestaResult.Resultado"/> preenchido.</summary>
    Ok,
    /// <summary>Produto-final nao tem receita cadastrada — calculo pulado, UI mostra placeholder.</summary>
    SemReceita,
    /// <summary>Erro de calculo (rendimento invalido, unidade incompativel, etc). <see cref="ItemCestaResult.Erro"/> tem o motivo.</summary>
    Erro
}

public sealed record ItemCestaResult(
    Guid ProdutoFinalId,
    string ProdutoNome,
    ItemCestaStatus Status,
    CalcularProducaoResult? Resultado,
    string? Erro);

/// <summary>
/// Insumo agregado entre todos os itens da cesta. Quando 2 produtos usam o mesmo insumo em
/// unidades diferentes mas conversiveis (g, kg), soma na unidade do primeiro encontrado. Quando
/// a conversao falha entre unidades incompativeis (Cx vs g), marca ConversaoFalhou=true e Falta=null.
/// </summary>
public sealed record InsumoConsolidadoResult(
    Guid InsumoId,
    string InsumoNome,
    decimal Precisa,
    UnidadeMedida UnidadeReceita,
    decimal Saldo,
    UnidadeMedida UnidadeSaldo,
    decimal? Falta,
    decimal? CustoEstimado,
    bool ConversaoFalhou,
    string? Aviso);
