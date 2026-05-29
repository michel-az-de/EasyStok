namespace EasyStock.Application.UseCases.Inteligencia.Board;

public sealed record GetBoardCommand(
    [property: Required] Guid EmpresaId,
    [property: Range(1, 365)] int Periodo = 30);
