using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.CalcularProducao;

public sealed record CalcularProducaoCommand(
    Guid EmpresaId,
    Guid ProdutoFinalId,
    decimal QuantidadeDesejada,
    UnidadeMedida UnidadeDesejada,
    Guid? LojaId);
