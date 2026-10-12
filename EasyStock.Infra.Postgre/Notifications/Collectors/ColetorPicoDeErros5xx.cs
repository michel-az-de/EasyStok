using System.Diagnostics.Metrics;
using System.Globalization;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Notifications.Collectors;

/// <summary>
/// Pico de erros 5xx da API (N10). Conta <c>system_error_logs</c> com <c>Source = 'api_backend'</c> e <c>Level = 'error'</c>
/// na janela (padrão 5 min) e abre o incidente acima do <c>Limite</c> (padrão 20). Lê só o <c>COUNT</c>: <c>Message</c> e
/// <c>Details</c> guardam exceção e pilha e nunca saem do banco. A tabela não tem <c>EmpresaId</c> (fora da RLS), então o
/// Worker lê sem bypass. O estado vive em <c>endpoint_health_state</c> com o nome <c>sistema/5xx</c>; acima do limite a
/// cada rodada o publicador re-emite e a dedupe de 15 min do outbox faz de um aviso por janela.
/// Configuração: <c>Notifications:Incidentes:Erros5xx:JanelaMinutos</c> e <c>:Limite</c>.
/// </summary>
public sealed class ColetorPicoDeErros5xx(
    EasyStockDbContext db,
    IPublicadorIncidenteSistema publicador,
    IConfiguration configuration,
    TimeProvider relogio,
    ILogger<ColetorPicoDeErros5xx> logger) : IColetorEventoNotificacao
{
    public const string NomeDoEstado = "sistema/5xx";
    public const string ChaveJanela = "Notifications:Incidentes:Erros5xx:JanelaMinutos";
    public const string ChaveLimite = "Notifications:Incidentes:Erros5xx:Limite";
    public const int JanelaPadraoMinutos = 5;
    public const int LimitePadrao = 20;

    /// <summary>Abre na primeira rodada acima do limite; resolve depois de 2 rodadas boas seguidas.</summary>
    private static readonly LimiaresIncidente Limiares = new(FalhasParaAbrir: 1, BoasParaResolver: 2);

    private static readonly Meter Meter = new("EasyStock.Notifications", "1.0");
    private static readonly Counter<long> Picos = Meter.CreateCounter<long>(
        "notifications.collector.spikes_5xx", "spikes", "Rodadas do coletor de 5xx acima do limite");

    public async Task ColetarAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var janela = Inteiro(ChaveJanela, JanelaPadraoMinutos);
        var limite = Inteiro(ChaveLimite, LimitePadrao);

        var erros = await db.SystemErrorLogs
            .Where(l => l.Source == "api_backend" && l.Level == "error" && l.CriadoEm >= agora.AddMinutes(-janela))
            .CountAsync(ct);

        var estado = await db.EndpointHealthStates.FirstOrDefaultAsync(s => s.EndpointName == NomeDoEstado, ct);
        if (estado is null)
        {
            estado = new EndpointHealthState { EndpointName = NomeDoEstado };
            db.EndpointHealthStates.Add(estado);
        }

        var avaliacao = AvaliadorIncidente.Avaliar(estado, saudavel: erros <= limite, agora, Limiares);
        if (erros > limite) estado.LastFailureMessage = $"5XX_{erros}";

        switch (avaliacao.Decisao)
        {
            case DecisaoIncidente.Abrir:
            case DecisaoIncidente.Reavisar:
                Picos.Add(1);
                logger.LogWarning("Pico de 5xx: {Erros} erros em {Janela} min (limite {Limite}).", erros, janela, limite);
                await publicador.PublicarAsync(ComponenteIncidente.Erros5xx, EstadoIncidente.ComProblema,
                    SeveridadeIncidente.Alta, avaliacao.DesdeUtc ?? agora, ct);
                break;
            case DecisaoIncidente.Resolver:
                logger.LogInformation("Pico de 5xx normalizado ({Erros} erros em {Janela} min).", erros, janela);
                await publicador.PublicarAsync(ComponenteIncidente.Erros5xx, EstadoIncidente.Normalizado,
                    SeveridadeIncidente.Media, avaliacao.DesdeUtc ?? agora, ct);
                break;
        }

        // O publicador commita a mesma unidade de trabalho; sem publicacao (interruptor, sem empresa padrao) ou sem
        // decisao, o estado ainda precisa ser gravado.
        await db.SaveChangesAsync(ct);
    }

    private int Inteiro(string chave, int padrao) =>
        int.TryParse(configuration[chave], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : padrao;
}
