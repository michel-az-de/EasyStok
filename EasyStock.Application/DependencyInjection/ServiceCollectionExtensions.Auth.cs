// Camada de Autenticação e Autorização
// Registra UseCases relacionados a: login, logout, tokens, recuperação de senha

using Microsoft.Extensions.DependencyInjection;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.CadastrarUsuario;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Application.UseCases.ConfirmEmail;
using EasyStock.Application.UseCases.ObterUsuarioAtual;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.AlterarSenha;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensão de ServiceCollection para registrar UseCases de Autenticação e Autorização.
/// Faz parte da divisão de responsabilidades do ServiceCollectionExtensions.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os UseCases de autenticação, autorização e gerenciamento de credenciais.
    /// </summary>
    public static IServiceCollection AddEasyStockAuthenticationUseCases(this IServiceCollection services)
    {
        // Fluxos de login e sessão
        services.AddScoped<AutenticarUsuarioUseCase>();
        services.AddScoped<CadastrarUsuarioUseCase>();
        services.AddScoped<RefreshTokenUseCase>();
        services.AddScoped<LogoutUseCase>();

        // Recuperação e reset de senha
        services.AddScoped<EsqueciSenhaUseCase>();
        services.AddScoped<ResetarSenhaUseCase>();
        services.AddScoped<ConfirmEmailUseCase>();

        // Perfil do usuário autenticado
        services.AddScoped<ObterUsuarioAtualUseCase>();
        services.AddScoped<AtualizarUsuarioAtualUseCase>();
        services.AddScoped<AlterarSenhaUseCase>();

        // Dados pessoais
        services.AddScoped<EasyStock.Application.UseCases.AnonimizarMeusDados.AnonimizarMeusDadosUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.ExportarMeusDados.ExportarMeusDadosUseCase>();

        // LGPD operacional (admin dispara em nome do cliente)
        services.AddScoped<EasyStock.Application.UseCases.Admin.AnonimizarUsuarioPorAdmin.AnonimizarUsuarioPorAdminUseCase>();

        // Cadastro manual via back-office (operador SuperAdmin)
        services.AddScoped<EasyStock.Application.UseCases.Admin.CriarTenantPorAdmin.CriarTenantPorAdminUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Admin.CriarUsuarioTenantPorAdmin.CriarUsuarioTenantPorAdminUseCase>();

        return services;
    }
}
