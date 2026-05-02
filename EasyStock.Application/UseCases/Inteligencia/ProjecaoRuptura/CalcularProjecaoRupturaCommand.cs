using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Inteligencia.ProjecaoRuptura;

public sealed record CalcularProjecaoRupturaCommand(
    [property: Required] Guid EmpresaId,
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20);
