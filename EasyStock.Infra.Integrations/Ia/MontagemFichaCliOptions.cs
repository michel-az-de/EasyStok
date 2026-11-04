namespace EasyStock.Infra.Integrations.Ia;

/// <summary>Opt-in administrativo exclusivo da ficha. Não recebe chave nem caminho de credenciais.</summary>
public sealed class MontagemFichaCliOptions
{
    public const string Secao = "MontagemFicha:Cli";
    public bool Enabled { get; set; }
    public string? ExecutablePath { get; set; }
    public int TimeoutSegundos { get; set; } = 60;
    public int MaxStdoutBytes { get; set; } = 131072;
    public int MaxStderrBytes { get; set; } = 8192;
}
