namespace EasyStock.Application.UseCases.Analytics.ReceitaCusto;

public sealed record GetReceitaCustoCommand(
    [property: Required] Guid EmpresaId,
    [property: Range(1, 365)] int PeriodoDias = 30,
    Guid? LojaId = null,
    int TimezoneOffsetMinutes = 0);
