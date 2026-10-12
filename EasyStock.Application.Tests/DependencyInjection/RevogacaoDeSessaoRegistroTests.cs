using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Auth;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.AceitarConvite;
using EasyStock.Application.UseCases.AlterarSenha;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.CriarUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.ReenviarConvite;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AlterarSenhaUsuarioCommand = EasyStock.Application.UseCases.AlterarSenhaUsuario.AlterarSenhaCommand;
using AlterarSenhaUsuarioUseCase = EasyStock.Application.UseCases.AlterarSenhaUsuario.AlterarSenhaUsuarioUseCase;

namespace EasyStock.Application.Tests.DependencyInjection;

/// <summary>
/// #1352 (N7): o <see cref="RevogadorSessoes"/> e os cinco use cases que o chamam resolvem pelo registro de
/// produção, com as portas de infraestrutura trocadas por substitutos. Constructor novo sem registro só
/// apareceria como 500 na rota (foi assim que o teste do /register do N0 pegou o primeiro caso).
/// </summary>
public class RevogacaoDeSessaoRegistroTests
{
    [Fact]
    public void RevogadorEOsCincoQueRevogamResolvemPeloRegistro()
    {
        var servicos = new ServiceCollection()
            .AddLogging()
            .AddEasyStockAuthenticationUseCases()
            .AddEasyStockUserManagementUseCases()
            .AddSingleton(Substitute.For<IUsuarioRepository>())
            .AddSingleton(Substitute.For<IRefreshTokenRepository>())
            .AddSingleton(Substitute.For<IResetTokenRepository>())
            .AddSingleton(Substitute.For<IAuditLogRepository>())
            .AddSingleton(Substitute.For<IUsuarioEmpresaRepository>())
            .AddSingleton(Substitute.For<IUsuarioPerfilRepository>())
            .AddSingleton(Substitute.For<ICacheService>())
            .AddSingleton(Substitute.For<ICurrentUserAccessor>())
            .AddSingleton(Substitute.For<IUnitOfWork>())
            .AddSingleton(Substitute.For<IPasswordHasher>())
            // N8: o reset avisa a troca de senha pelo motor e o pedido le consentimento e configuracao.
            .AddSingleton(Substitute.For<INotificadorService>())
            .AddSingleton(Substitute.For<IEmpresaPadraoResolver>())
            .AddSingleton(Substitute.For<ITenantContextAccessor>())
            .AddSingleton(Substitute.For<IConsentimentoRepository>())
            // N9: o convite le o nome da empresa, o perfil (superadmin nunca nasce por convite) e a assinatura do plano.
            .AddSingleton(Substitute.For<IEmpresaRepository>())
            .AddSingleton(Substitute.For<IPerfilRepository>())
            .AddSingleton(Substitute.For<IAssinaturaEmpresaRepository>())
            .AddSingleton(Substitute.For<IGoogleIdTokenValidator>())
            .AddSingleton(Substitute.For<IEmailConfirmationTokenRepository>())
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IValidator<AlterarSenhaUsuarioCommand>>(new AlterarSenhaUsuarioCommandValidator());

        using var provider = servicos.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var escopo = provider.CreateScope();
        var resolver = escopo.ServiceProvider;

        resolver.GetRequiredService<RevogadorSessoes>().Should().NotBeNull();
        resolver.GetRequiredService<ResetarSenhaUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<ResetarSenhaPorCodigoUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<EsqueciSenhaUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<LimpezaTokensAcesso>().Should().NotBeNull();
        resolver.GetRequiredService<AlterarSenhaUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<AlterarSenhaUsuarioUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<DesativarUsuarioUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<AtribuirPerfilUsuarioUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<ConvitesDeAcesso>().Should().NotBeNull();
        resolver.GetRequiredService<AceitarConviteUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<ReenviarConviteUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<CriarUsuarioUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<AtualizarUsuarioUseCase>().Should().NotBeNull();
        resolver.GetRequiredService<IdentificarUsuarioGoogleUseCase>().Should().NotBeNull();
    }
}
