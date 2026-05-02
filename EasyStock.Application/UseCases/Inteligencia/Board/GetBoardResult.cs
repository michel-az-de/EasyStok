namespace EasyStock.Application.UseCases.Inteligencia.Board;

public sealed record GetBoardResult(
    Guid EmpresaId,
    int Periodo,
    long QuantidadeEmEstoque,
    decimal ValorTotalEstoque,
    decimal MediaVendasDiaria,
    decimal ProjecaoVendasPeriodo,
    decimal ProjecaoReceitaPeriodo);
