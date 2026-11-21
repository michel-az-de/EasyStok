using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyStock.Api.Observability.HealthChecks;

/// <summary>
/// Espaço livre do disco onde o contêiner roda (#2376). Na VPS, o "/" do contêiner é o mesmo sistema de arquivos dos
/// volumes do Postgres e do OpenObserve, e o OpenObserve só limita por tempo. Fica no <c>/health/disco</c>, fora do
/// <c>/health</c> e do <c>/health/ready</c>: disco apertado avisa pelo vigia externo, mas não tira a API do ar.
/// </summary>
public sealed class DiscoHealthCheck(Func<(long Livre, long Total)> medir, double minimoLivrePercentual) : IHealthCheck
{
    public const string Tag = "disco";

    /// <summary>Mede o "/" (ou a raiz do diretório atual fora do Linux) com o mínimo de <c>Observabilidade:Disco:MinimoLivrePercentual</c>.</summary>
    public static DiscoHealthCheck DoSistema(IConfiguration configuration)
    {
        var raiz = OperatingSystem.IsWindows() ? Path.GetPathRoot(Environment.CurrentDirectory)! : "/";
        var minimo = configuration.GetValue("Observabilidade:Disco:MinimoLivrePercentual", 15d);
        return new DiscoHealthCheck(() =>
        {
            var disco = new DriveInfo(raiz);
            return (disco.AvailableFreeSpace, disco.TotalSize);
        }, minimo);
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        long livre, total;
        try
        {
            (livre, total) = medir();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Não foi possível medir o disco", ex));
        }

        var percentual = total > 0 ? Math.Round(100d * livre / total, 1) : 0;
        var dados = new Dictionary<string, object>
        {
            ["livrePercentual"] = percentual,
            ["livreGb"] = Math.Round(livre / 1073741824d, 1),
            ["minimoLivrePercentual"] = minimoLivrePercentual,
        };
        var texto = string.Create(CultureInfo.InvariantCulture, $"{percentual}% livre, mínimo {minimoLivrePercentual}%");
        return Task.FromResult(percentual < minimoLivrePercentual
            ? HealthCheckResult.Unhealthy(texto, data: dados)
            : HealthCheckResult.Healthy(texto, dados));
    }
}
