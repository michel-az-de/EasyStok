using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Analytics.DashboardFull;

public sealed record GetDashboardFullCommand(
    [property: Required] Guid EmpresaId,
    [property: Range(1, 365)] int PeriodoDias = 30,
    Guid? LojaId = null,
    int TimezoneOffsetMinutes = 0);
