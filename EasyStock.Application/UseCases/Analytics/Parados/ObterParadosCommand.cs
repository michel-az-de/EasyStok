using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Analytics.Parados;

public sealed record ObterParadosCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    int? DiasSemMovimento = null,
    [property: Range(1, 1000)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20);
