using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Analytics.Validade;

public sealed record ObterValidadeCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    int? Dias = null,
    [property: Range(1, 1000)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20);
