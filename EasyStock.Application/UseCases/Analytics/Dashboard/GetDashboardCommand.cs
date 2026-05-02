using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Analytics.Dashboard;

public sealed record GetDashboardCommand(
    [property: Required] Guid EmpresaId,
    [property: Range(1, 365)] int PeriodoDias = 30,
    Guid? LojaId = null);
