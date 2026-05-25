namespace EasyStock.Domain.Reporting;

/// <summary>Formatos de saída suportados pelo motor de relatórios.</summary>
public enum ReportFormat : short
{
    Csv = 1,
    Xlsx = 2,
    Pdf = 3,
    Zip = 4,
}
