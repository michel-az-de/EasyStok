namespace EasyStock.Api.Configuration;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool EnableAlertasEstoqueJob { get; set; }
    public bool EnableProcessarRecebimentoJob { get; set; }
    public bool EnableRecalcularVelocidadesJob { get; set; }
    public bool EnableRelatorioMensalJob { get; set; }
    public bool EnableDiagnosticoEmailReport { get; set; }
}
