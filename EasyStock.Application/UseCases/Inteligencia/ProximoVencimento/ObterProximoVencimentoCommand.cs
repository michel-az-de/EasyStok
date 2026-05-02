using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Inteligencia.ProximoVencimento;

public sealed record ObterProximoVencimentoCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    int? Dias = null,
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20);
