namespace EasyStock.Application.UseCases.Analytics.AlertasDias;

public sealed record ObterDiasAlertaValidadeCommand(
    Guid? LojaId = null);
