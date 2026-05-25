using EasyStock.Application.Ports.Output.Storage;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Salva logs automaticamente no storage de arquivos a cada 30 minutos.
/// Garante que os logs estejam sempre persistidos mesmo se o App Service reciclar.
/// </summary>
public sealed class LogStorageBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<LogStorageBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private string GetLogDirectory() =>
        configuration["LogSettings:LogDirectory"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "logs");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("LogStorageBackgroundService iniciado — logs serão salvos no storage a cada {Interval} minutos.", Interval.TotalMinutes);

        // Garantir que o diretório de logs local existe
        var logsDir = GetLogDirectory();
        try
        {
            Directory.CreateDirectory(logsDir);
            logger.LogInformation("Diretório de logs garantido: {LogsDir}", logsDir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível criar diretório de logs: {LogsDir}", logsDir);
        }

        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SalvarLogsNoStorageAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erro ao salvar logs no storage automaticamente.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task SalvarLogsNoStorageAsync(CancellationToken ct)
    {
        var logsDir = GetLogDirectory();
        if (!Directory.Exists(logsDir))
        {
            logger.LogDebug("Diretório de logs não encontrado: {LogsDir}", logsDir);
            return;
        }

        var arquivos = new DirectoryInfo(logsDir)
            .GetFiles("easystock-*.log")
            .Where(f => f.Length > 0)
            .OrderBy(f => f.Name)
            .ToArray();

        if (arquivos.Length == 0)
        {
            logger.LogDebug("Nenhum arquivo de log para salvar no storage.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        foreach (var arquivo in arquivos)
        {
            try
            {
                // Ler o arquivo com share mode para não conflitar com Serilog
                byte[] content;
                using (var fs = new FileStream(arquivo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var ms = new MemoryStream())
                {
                    await fs.CopyToAsync(ms, ct);
                    content = ms.ToArray();
                }

                if (content.Length == 0) continue;

                await fileStorage.UploadAsync(new FileUploadRequest(
                    BucketPath: "logs",
                    FileName: arquivo.Name,
                    ContentType: "text/plain",
                    Content: content,
                    IsPublic: false), ct);

                logger.LogDebug("Log salvo no storage: logs/{FileName} ({Size} bytes)", arquivo.Name, content.Length);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao salvar {FileName} no storage.", arquivo.Name);
            }
        }

        logger.LogInformation("Backup automático de {Count} arquivo(s) de log concluído.", arquivos.Length);
    }
}
