// Camada Admin Storefront (TASK-EZ-ADMIN-001 — Fase 7).
// Registra UseCases de CRUD admin de Storefront + Cardápio.

using Microsoft.Extensions.DependencyInjection;
using EasyStock.Application.UseCases.Admin.Storefront.AtivarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ListarCardapioAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ReordenarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleDisponibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.CriarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.DesativarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.EditarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.ListarStorefrontsAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.ObterStorefrontAdmin;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra os UseCases admin para gestão de Storefront + Cardápio
    /// (TASK-EZ-ADMIN-001, Fase 7 do plano). Consumidos pelos controllers
    /// AdminStorefrontController e AdminStorefrontCardapioController.
    /// </summary>
    public static IServiceCollection AddEasyStockAdminStorefrontUseCases(this IServiceCollection services)
    {
        // Storefront CRUD
        services.AddScoped<ListarStorefrontsAdminUseCase>();
        services.AddScoped<ObterStorefrontAdminUseCase>();
        services.AddScoped<CriarStorefrontAdminUseCase>();
        services.AddScoped<EditarStorefrontAdminUseCase>();
        services.AddScoped<AtivarStorefrontAdminUseCase>();
        services.AddScoped<DesativarStorefrontAdminUseCase>();

        // Cardápio CRUD
        services.AddScoped<ListarCardapioAdminUseCase>();
        services.AddScoped<AdicionarCardapioItemAdminUseCase>();
        services.AddScoped<EditarCardapioItemAdminUseCase>();
        services.AddScoped<ToggleVisibilidadeCardapioItemAdminUseCase>();
        services.AddScoped<ToggleDisponibilidadeCardapioItemAdminUseCase>();
        services.AddScoped<ReordenarCardapioItemAdminUseCase>();

        return services;
    }
}
