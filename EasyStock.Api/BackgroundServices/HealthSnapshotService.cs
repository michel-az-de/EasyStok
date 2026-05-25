using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Caching.Distributed;

namespace EasyStock.Api.BackgroundServices;

public sealed class HealthSnapshotService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IDistributedCache cache,
    ILogger<HealthSnapshotService> logger) : BackgroundService
{
    private readonly ConcurrentQueue<HealthSnapshot> _snapshots = new();
    private const int MaxSnapshots = 120; // 2h at 60s intervals
    private const string RedisHistoryKey = "healthsnap:history";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public IReadOnlyList<HealthSnapshot> GetSnapshots() => _snapshots.ToArray();

    /// <summary>
    /// Descarta todo o histórico de snapshots em memória e no Redis.
    /// Útil para obter uma linha de base limpa após deploy ou correção de problemas.
    /// </summary>
    public async Task ZerarHistoricoAsync(CancellationToken ct = default)
    {
        while (_snapshots.TryDequeue(out _)) { }
        try { await cache.RemoveAsync(RedisHistoryKey, ct); } catch { /* best-effort */ }
        logger.LogInformation("Histórico de health snapshots zerado manualmente.");
    }

    private string GetLogDirectory() =>
        configuration["LogSettings:LogDirectory"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "logs");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HealthSnapshotService iniciado.");

        // Restore history from Redis immediately so charts have data on load
        await LoadSnapshotsFromRedisAsync(stoppingToken);

        // Short warm-up delay before first fresh snapshot
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await CaptureSnapshotAsync(stoppingToken);
                _snapshots.Enqueue(snapshot);

                while (_snapshots.Count > MaxSnapshots)
                    _snapshots.TryDequeue(out _);

                await PersistSnapshotsToRedisAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erro ao capturar health snapshot.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task LoadSnapshotsFromRedisAsync(CancellationToken ct)
    {
        try
        {
            var json = await cache.GetStringAsync(RedisHistoryKey, ct);
            if (json is null) return;
            var loaded = JsonSerializer.Deserialize<HealthSnapshot[]>(json);
            if (loaded is null) return;
            foreach (var snap in loaded) _snapshots.Enqueue(snap);
            while (_snapshots.Count > MaxSnapshots) _snapshots.TryDequeue(out _);
            logger.LogInformation("Carregados {Count} snapshots do Redis.", loaded.Length);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nao foi possivel carregar snapshots do Redis.");
        }
    }

    private async Task PersistSnapshotsToRedisAsync(CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(_snapshots.ToArray());
            await cache.SetStringAsync(RedisHistoryKey, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2) }, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Nao foi possivel persistir snapshots no Redis.");
        }
    }

    private async Task<HealthSnapshot> CaptureSnapshotAsync(CancellationToken ct)
    {
        var snapshot = new HealthSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            OverallStatus = "ok"
        };

        // Test DB
        try
        {
            var sw = Stopwatch.StartNew();
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetService<EasyStockDbContext>();
            if (dbContext is not null)
            {
                var canConnect = await dbContext.Database.CanConnectAsync(ct);
                sw.Stop();
                snapshot.DbLatencyMs = sw.ElapsedMilliseconds;
                snapshot.DbStatus = canConnect ? "ok" : "falha";
                if (!canConnect) snapshot.OverallStatus = "critical";
            }
            else
            {
                snapshot.DbStatus = "sem_efcore";
            }
        }
        catch (Exception ex)
        {
            snapshot.DbLatencyMs = -1;
            snapshot.DbStatus = "falha";
            snapshot.OverallStatus = "critical";
            logger.LogDebug(ex, "DB health check failed.");
        }

        // Test Redis
        try
        {
            var redisCs = configuration.GetConnectionString("Redis");
            if (!string.IsNullOrWhiteSpace(redisCs))
            {
                var sw = Stopwatch.StartNew();
                using var scope = scopeFactory.CreateScope();
                var cache = scope.ServiceProvider.GetService<IDistributedCache>();
                if (cache is not null)
                {
                    var key = "healthsnap:ping:" + Guid.NewGuid().ToString("N")[..6];
                    await cache.SetStringAsync(key, "1", new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5)
                    }, ct);
                    await cache.GetStringAsync(key, ct);
                    sw.Stop();
                    snapshot.RedisLatencyMs = sw.ElapsedMilliseconds;
                    snapshot.RedisStatus = "ok";
                }
            }
        }
        catch
        {
            snapshot.RedisLatencyMs = -1;
            snapshot.RedisStatus = "falha";
            if (snapshot.OverallStatus == "ok") snapshot.OverallStatus = "degraded";
        }

        // Count errors from log file (last 60s)
        snapshot.ErrorCount = CountRecentErrors();

        if (snapshot.ErrorCount > 0 && snapshot.OverallStatus == "ok")
            snapshot.OverallStatus = "degraded";

        return snapshot;
    }

    private int CountRecentErrors()
    {
        try
        {
            var logsDir = GetLogDirectory();
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var logFile = Path.Combine(logsDir, $"easystock-{today}.log");

            if (!File.Exists(logFile)) return 0;

            var cutoff = DateTime.UtcNow.AddSeconds(-65); // 65s to account for timing
            var count = 0;
            var tsRegex = new Regex(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (ERR|FTL)\]");

            using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            // Read from end - seek near end for efficiency
            if (fs.Length > 50_000)
                fs.Seek(-50_000, SeekOrigin.End);

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line is null) continue;

                var match = tsRegex.Match(line);
                if (!match.Success) continue;

                if (DateTime.TryParse(match.Groups[1].Value, out var ts) && ts >= cutoff)
                    count++;
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }
}

public sealed class HealthSnapshot
{
    public DateTimeOffset Timestamp { get; set; }
    public long DbLatencyMs { get; set; }
    public string DbStatus { get; set; } = "ok";
    public long? RedisLatencyMs { get; set; }
    public string? RedisStatus { get; set; }
    public int ErrorCount { get; set; }
    public string OverallStatus { get; set; } = "ok";
}
