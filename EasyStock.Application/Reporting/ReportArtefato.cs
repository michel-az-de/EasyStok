using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting;

/// <summary>Nome e tipo do artefato de um relatório: vêm do que foi gravado (formato da run), nunca da requisição.</summary>
public static class ReportArtefato
{
    public static string Extensao(ReportFormat formato) => formato switch
    {
        ReportFormat.Csv  => ".csv",
        ReportFormat.Xlsx => ".xlsx",
        ReportFormat.Pdf  => ".pdf",
        ReportFormat.Zip  => ".zip",
        _                 => ".bin",
    };

    public static string ContentType(ReportFormat formato) => formato switch
    {
        ReportFormat.Csv  => "text/csv; charset=utf-8",
        ReportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ReportFormat.Pdf  => "application/pdf",
        ReportFormat.Zip  => "application/zip",
        _                 => "application/octet-stream",
    };

    /// <summary>Só letra ASCII, dígito, '-' e '_': nada que quebre o Content-Disposition.</summary>
    public static string NomeArquivo(ReportRun run)
    {
        var chave = new string(run.ReportKey.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '-').ToArray());
        return $"{chave}_{run.Id:N}{Extensao(run.Format)}";
    }
}
