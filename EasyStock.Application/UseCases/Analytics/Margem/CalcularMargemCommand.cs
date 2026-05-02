using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Analytics.Margem;

public sealed record CalcularMargemCommand(
    [property: Required] Guid EmpresaId,
    [property: Range(1, 365)] int Dias = 30,
    [property: Range(1, 1000)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20,
    Guid? LojaId = null);
