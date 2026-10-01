// Integrações por loja (F16, #1246): chaves cifradas, botão Testar e o vigia de 15 min.

using EasyStock.Application.UseCases.Integracoes;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Registra os UseCases da tela de Integrações e do vigia.</summary>
    public static IServiceCollection AddEasyStockIntegracoesUseCases(this IServiceCollection services)
    {
        services.AddScoped<ExecutorTesteIntegracao>();
        services.AddScoped<ListarIntegracoesUseCase>();
        services.AddScoped<SalvarChaveIntegracaoUseCase>();
        services.AddScoped<TestarIntegracaoUseCase>();
        services.AddScoped<DesativarIntegracaoUseCase>();
        services.AddScoped<VigiarIntegracoesUseCase>();
        return services;
    }
}
