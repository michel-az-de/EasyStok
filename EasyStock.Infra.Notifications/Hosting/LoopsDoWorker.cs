using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// #2536: batidas dos loops do Worker fora do motor de notificações (outbox, agendamento, relatórios...). O loop chama
/// <see cref="Iniciar"/> quando passa a rodar e <see cref="Bater"/> no começo de cada iteração; iteração travada para de bater. Loop desligado por configuração nunca se registra e não conta. Singleton.
/// </summary>
public sealed class LoopsDoWorker(TimeProvider time)
{
    /// <summary>Piso da janela: cobre a espera inicial dos loops e uma iteração lenta (outbox com HTTP, relatório).</summary>
    public static readonly TimeSpan PisoDaJanela = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Registro> _loops = new(StringComparer.Ordinal);

    public LoopsDoWorker() : this(TimeProvider.System) { }

    public void Iniciar(string loop, TimeSpan intervalo)
    {
        var janela = TimeSpan.FromTicks(Math.Max(intervalo.Ticks * 5, PisoDaJanela.Ticks));
        _loops[loop] = new Registro(janela, time.GetUtcNow());
    }

    public void Bater(string loop)
    {
        if (_loops.TryGetValue(loop, out var registro))
            _loops[loop] = registro with { UltimaBatida = time.GetUtcNow() };
    }

    /// <summary>Loops registrados que passaram da janela sem bater, com o tempo parado.</summary>
    public IReadOnlyList<(string Loop, TimeSpan Parado, TimeSpan Janela)> Atrasados()
    {
        var agora = time.GetUtcNow();
        return _loops
            .Select(par => (Loop: par.Key, Parado: agora - par.Value.UltimaBatida, par.Value.Janela))
            .Where(l => l.Parado > l.Janela)
            .OrderBy(l => l.Loop, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record Registro(TimeSpan Janela, DateTimeOffset UltimaBatida);
}

/// <summary>
/// #2536: Unhealthy quando algum loop de <see cref="LoopsDoWorker"/> passa da janela. Usa a tag <c>dispatcher</c> para
/// entrar no <see cref="ArquivoDeVidaService"/> e no ping do Healthchecks.io, como os loops do motor.
/// </summary>
public sealed class LoopsDoWorkerHealthCheck(LoopsDoWorker loops) : IHealthCheck
{
    public const string Nome = "worker-loops";
    public static readonly string[] Tags = ["dispatcher"];

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var atrasados = loops.Atrasados();
        return Task.FromResult(atrasados.Count == 0
            ? HealthCheckResult.Healthy("Loops do Worker batendo dentro da janela.")
            : HealthCheckResult.Unhealthy(string.Join("; ", atrasados.Select(l =>
                $"{l.Loop}: parado {(int)l.Parado.TotalSeconds}s > janela {(int)l.Janela.TotalSeconds}s"))));
    }
}
