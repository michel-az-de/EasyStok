using EasyStock.Domain.Reporting;

namespace EasyStock.Domain.Exceptions;

/// <summary>
/// Lançada quando uma transição de estado inválida é tentada em um <see cref="ReportRun"/>.
/// </summary>
public sealed class InvalidReportStateException : RegraDeDominioVioladaException
{
    public InvalidReportStateException(ReportStatus current, string attemptedTransition)
        : base($"Transição '{attemptedTransition}' inválida para ReportRun em estado '{current}'.")
    {
    }
}
