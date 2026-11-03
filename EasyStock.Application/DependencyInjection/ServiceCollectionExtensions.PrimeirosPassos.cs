using EasyStock.Application.UseCases.PrimeirosPassos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Configuração inicial da loja no servidor (#2070): estado do assistente "Primeiros passos".</summary>
    public static IServiceCollection AddEasyStockPrimeirosPassosUseCases(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AutoridadeNaConfiguracaoInicial>();
        services.AddScoped<ObterConfiguracaoInicialUseCase>();
        services.AddScoped<MarcarPassoConfiguracaoInicialUseCase>();
        services.AddScoped<AdiarConfiguracaoInicialUseCase>();
        services.AddScoped<ConcluirConfiguracaoInicialUseCase>();
        return services;
    }
}
