using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Inteligencia.Rotatividade;

public sealed record CalcularRotatividadeCommand(
    [property: Required] Guid EmpresaId,
    Guid? ProdutoId = null,
    [property: Range(1, 365)] int DiasHistorico = 30);
