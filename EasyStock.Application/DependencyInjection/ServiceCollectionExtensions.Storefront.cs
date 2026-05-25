using EasyStock.Application.UseCases.Storefront.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases do storefront (autenticação, agendamento, menu, etc.).
    /// </summary>
    public static IServiceCollection AddEasyStockStorefrontUseCases(this IServiceCollection services)
    {
        // Autenticação via OTP (EZ-AUTH-001, EZ-AUTH-002)
        services.AddScoped<ValidarOtpUseCase>();

        // TimeProvider: TimeProvider.System como singleton — entities e use cases
        // storefront usam injetado para testes determinísticos.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
