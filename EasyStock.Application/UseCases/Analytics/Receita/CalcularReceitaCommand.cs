using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Analytics.Receita;

public sealed record CalcularReceitaCommand(
    [property: Required] Guid EmpresaId,
    [property: Range(1, 36)] int Meses = 12,
    Guid? LojaId = null);
