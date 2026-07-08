// Camada Banners — broadcast global de plataforma (#869). Cadastrado no Admin (SuperAdmin),
// exibido no app Web. Tabelas globais sem EmpresaId.

using EasyStock.Application.UseCases.Banners;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Registra os UseCases de banners (CRUD admin + interações do usuário no Web).</summary>
    public static IServiceCollection AddEasyStockBannerUseCases(this IServiceCollection services)
    {
        // Admin (SuperAdmin)
        services.AddScoped<CriarBannerUseCase>();
        services.AddScoped<AtualizarBannerUseCase>();
        services.AddScoped<AtivarBannerUseCase>();
        services.AddScoped<DesativarBannerUseCase>();
        services.AddScoped<DeletarBannerUseCase>();
        services.AddScoped<ListarBannersAdminUseCase>();
        services.AddScoped<ObterBannerUseCase>();

        // Web (usuário logado)
        services.AddScoped<ListarBannersAtivosUseCase>();
        services.AddScoped<RegistrarInteracaoBannerUseCase>();

        return services;
    }
}
