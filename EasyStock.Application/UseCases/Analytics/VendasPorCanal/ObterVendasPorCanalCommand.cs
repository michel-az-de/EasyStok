using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Analytics.VendasPorCanal;

public sealed record ObterVendasPorCanalCommand(
    [property: Required] Guid EmpresaId,
    DateTime? DataDe = null,
    DateTime? DataAte = null,
    [property: Range(1, 365)] int DiasPadrao = 30,
    Guid? LojaId = null);
