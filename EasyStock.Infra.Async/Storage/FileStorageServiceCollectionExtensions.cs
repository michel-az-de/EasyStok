using EasyStock.Application.Ports.Output.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Async.Storage;

public static class FileStorageServiceCollectionExtensions
{
    /// <summary>
    /// Seção de configuração canônica do storage de arquivos. Igual à constante
    /// <c>ConfigurationKeys.SectionFileStorage</c> da API — mantida aqui porque o
    /// Infra.Async não referencia o projeto Api (a dependência seria invertida).
    /// </summary>
    public const string SectionFileStorage = "FileStorage";

    /// <summary>
    /// Registra <see cref="IFileStorage"/> pelo provider configurado em "FileStorage:Provider"
    /// (Local | S3 | AzureFileShare). Compartilhado entre API e Worker — antes só a API
    /// registrava, o que deixava o motor de relatórios do Worker sem resolver IFileStorage
    /// (falha em runtime ao gerar relatório). Lifetime Singleton, idêntico ao registro original.
    /// </summary>
    public static IServiceCollection AddEasyStockFileStorageCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStorageOptions>(configuration.GetSection(SectionFileStorage));

        var fileStorageOptions = configuration
            .GetSection(SectionFileStorage)
            .Get<FileStorageOptions>() ?? new FileStorageOptions();

        if (string.Equals(fileStorageOptions.Provider, "AzureFileShare", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, AzureFileShareStorage>();
        else if (string.Equals(fileStorageOptions.Provider, "S3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, S3CompatibleFileStorage>();
        else
            services.AddSingleton<IFileStorage, LocalFileStorage>();

        return services;
    }
}
