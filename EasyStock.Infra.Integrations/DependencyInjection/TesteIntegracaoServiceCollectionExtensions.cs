using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Infra.Integrations.Conexao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Teste de conexão e vigia das integrações (F16, #1246): um testador por provider (cliente HTTP
/// próprio, sem retry: o teste tem teto de 5 s no <see cref="ExecutorTesteIntegracao"/>), as chaves
/// globais da configuração e o estado em memória do último teste das chaves globais.
/// </summary>
public static class TesteIntegracaoServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockTestesIntegracao(this IServiceCollection services)
    {
        var timeoutExterno = TimeSpan.FromSeconds(10);

        services.AddHttpClient<TestadorMercadoPago>(c => c.Timeout = timeoutExterno);
        services.AddHttpClient<TestadorWhatsApp>(c => c.Timeout = timeoutExterno);
        services.AddHttpClient<TestadorLalamove>(c => c.Timeout = timeoutExterno);
        services.AddHttpClient<TestadorGoogleMaps>(c =>
        {
            c.BaseAddress = new Uri(GeocodingServiceCollectionExtensions.GoogleDefaultBaseUrl);
            c.Timeout = timeoutExterno;
        });

        services.AddScoped<ITestadorIntegracao>(sp => sp.GetRequiredService<TestadorMercadoPago>());
        services.AddScoped<ITestadorIntegracao>(sp => sp.GetRequiredService<TestadorWhatsApp>());
        services.AddScoped<ITestadorIntegracao>(sp => sp.GetRequiredService<TestadorLalamove>());
        services.AddScoped<ITestadorIntegracao>(sp => sp.GetRequiredService<TestadorGoogleMaps>());

        services.AddSingleton<IChavesGlobaisIntegracao, ChavesGlobaisIntegracao>();
        services.AddSingleton<IEstadoTesteIntegracaoStore, EstadoTesteIntegracaoEmMemoria>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
