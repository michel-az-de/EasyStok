namespace EasyStock.Application.UseCases.Analytics.DashboardExtras;

public sealed record GetDashboardExtrasCommand(
    Guid EmpresaId,
    int PeriodoDias,
    Guid? LojaId,
    int TimezoneOffsetMinutes);
