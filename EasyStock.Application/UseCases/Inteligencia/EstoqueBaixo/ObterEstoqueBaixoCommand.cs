using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Inteligencia.EstoqueBaixo;

public sealed record ObterEstoqueBaixoCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    int? Limite = null,
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20);
