using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.CalcularProducao;

/// <summary>
/// Calcula consumo de insumos para uma cesta (varios produtos-finais de uma vez).
/// Onda 1: in-context a partir de um Pedido. Tolerancia por item — falha de 1 nao derruba a cesta.
/// </summary>
public sealed record CalcularCestaProducaoCommand(
    Guid EmpresaId,
    Guid? LojaId,
    IReadOnlyList<ItemCestaInput> Itens);

public sealed record ItemCestaInput(
    Guid ProdutoFinalId,
    decimal Quantidade,
    UnidadeMedida Unidade);
