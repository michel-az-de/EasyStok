namespace EasyStock.Application.UseCases.Inteligencia.Rotatividade;

public sealed record RotatividadeResult(
    Guid EmpresaId,
    Guid? ProdutoId,
    int PeriodoDias,
    decimal TaxaSaidaDiaria,
    decimal TaxaSaidaSemanal,
    decimal TaxaSaidaMensal);
