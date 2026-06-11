// Camada de Gerenciamento de Usuários, Lojas e Empresas
// Registra UseCases relacionados a: CRUD de usuários, lojas, empresas, perfis

using Microsoft.Extensions.DependencyInjection;
using EasyStock.Application.UseCases.RegistrarEmpresa;
using EasyStock.Application.UseCases.CompletarOnboarding;
using EasyStock.Application.UseCases.CriarUsuario;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.Application.UseCases.AlterarSenhaUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.ListarUsuarios;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.CriarLoja;
using EasyStock.Application.UseCases.AtualizarLoja;
using EasyStock.Application.UseCases.DesativarLoja;
using EasyStock.Application.UseCases.ReativarLoja;
using EasyStock.Application.UseCases.ListarLojas;
using EasyStock.Application.UseCases.ConfiguracoesLoja;
using EasyStock.Application.UseCases.GerenciarUsuario;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensão de ServiceCollection para registrar UseCases de Gerenciamento de Usuários, Lojas e Empresas.
/// Faz parte da divisão de responsabilidades do ServiceCollectionExtensions.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os UseCases de gerenciamento de usuários, lojas, empresas e suas configurações.
    /// </summary>
    public static IServiceCollection AddEasyStockUserManagementUseCases(this IServiceCollection services)
    {
        // Registro e gerenciamento de empresas
        services.AddScoped<RegistrarEmpresaUseCase>();
        services.AddScoped<CompletarOnboardingUseCase>();

        // CRUD de usuários
        services.AddScoped<CriarUsuarioUseCase>();
        services.AddScoped<AtualizarUsuarioUseCase>();
        services.AddScoped<AlterarSenhaUsuarioUseCase>();
        services.AddScoped<DesativarUsuarioUseCase>();
        services.AddScoped<ListarUsuariosUseCase>();

        // Perfis e permissões
        services.AddScoped<AtribuirPerfilUsuarioUseCase>();

        // CRUD de lojas
        services.AddScoped<CriarLojaUseCase>();
        services.AddScoped<AtualizarLojaUseCase>();
        services.AddScoped<DesativarLojaUseCase>();
        services.AddScoped<ReativarLojaUseCase>();
        services.AddScoped<ListarLojasUseCase>();

        // Configurações de loja
        services.AddScoped<ObterConfiguracaoLojaUseCase>();
        services.AddScoped<AtualizarConfiguracaoLojaUseCase>();
        services.AddScoped<ResetarConfiguracaoLojaUseCase>();

        // Favoritos do menu lateral / "Meu dia" (ADR-0032, fatia 4)
        services.AddScoped<EasyStock.Application.UseCases.MenuFavoritos.ObterFavoritosMenuUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.MenuFavoritos.SalvarFavoritosMenuUseCase>();

        // Auditoria de usuários (deletar dados após LGPD)
        services.AddScoped<DeleteUserAuditDataUseCase>();

        return services;
    }
}
