using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensão de ServiceCollection para registrar todos os UseCases da aplicação.
///
/// Este arquivo é o orquestrador central que chama os métodos parciais definidos em:
/// - ServiceCollectionExtensions.Auth.cs (Autenticação)
/// - ServiceCollectionExtensions.Users.cs (Usuários e Lojas)
/// - ServiceCollectionExtensions.Fornecedores.cs (Fornecedores)
/// - ServiceCollectionExtensions.Clientes.cs (Clientes)
/// - ServiceCollectionExtensions.Core.cs (Pedidos, Caixa, Estoque, etc)
/// - ServiceCollectionExtensions.Analytics.cs (Analytics e Inteligência)
/// - ServiceCollectionExtensions.Notifications.cs (Notificações multi-canal)
///
/// A divisão melhora legibilidade e reduz usings desnecessários de ~103 para ~10 por arquivo.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os UseCases da camada Application.
    /// Chama os métodos de registro específicos por domínio.
    /// </summary>
    public static IServiceCollection AddEasyStockApplication(this IServiceCollection services)
    {
        // Registra UseCases em ordem de domínio para melhor organização
        services
            .AddEasyStockAuthenticationUseCases()
            .AddEasyStockUserManagementUseCases()
            .AddEasyStockClienteUseCases()
            .AddEasyStockFornecedorUseCases()
            .AddEasyStockCoreUseCases()
            .AddEasyStockAnalyticsUseCases()
            .AddEasyStockNotificationsUseCases()
            .AddEasyStockPublicUseCases();

        return services;
    }
}
