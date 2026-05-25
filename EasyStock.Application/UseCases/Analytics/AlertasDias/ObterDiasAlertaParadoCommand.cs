using System.ComponentModel.DataAnnotations;

namespace EasyStock.Application.UseCases.Analytics.AlertasDias;

public sealed record ObterDiasAlertaParadoCommand(
    Guid? LojaId = null);
