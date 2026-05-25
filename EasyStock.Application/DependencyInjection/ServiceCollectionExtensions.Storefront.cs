// Camada Storefront — UseCases públicos servidos pela loja virtual hospedada
// (cardápio, auth OTP, agendamento, checkout, webhooks, avaliações). Tenant é
// resolvido por slug na rota, não via JWT.

using EasyStock.Application.UseCases.Storefront.Menu;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases do storefront público — anônimos ou autenticados via
    /// sessão de cliente (não usuário do ERP). Slug resolve o tenant.
    /// </summary>
    public static IServiceCollection AddEasyStockStorefrontUseCases(this IServiceCollection services)
    {
        services.AddScoped<ListarCardapioPublicoUseCase>();
        return services;
    }
}
