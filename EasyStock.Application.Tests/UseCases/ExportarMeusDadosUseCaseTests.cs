using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.ValueObjects;
using EasyStock.Application.UseCases.ExportarMeusDados;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class ExportarMeusDadosUseCaseTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IUsuarioEmpresaRepository _usuarioEmpresaRepository = Substitute.For<IUsuarioEmpresaRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IConsentimentoRepository _consentimentos = Substitute.For<IConsentimentoRepository>();
    private readonly IPreferenciaNotificacaoRepository _preferencias = Substitute.For<IPreferenciaNotificacaoRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ILogger<ExportarMeusDadosUseCase> _logger = Substitute.For<ILogger<ExportarMeusDadosUseCase>>();

    private ExportarMeusDadosUseCase CriarUseCase() =>
        new(_usuarioRepository, _usuarioEmpresaRepository, _refreshTokenRepository, _consentimentos, _preferencias, _currentUser, _logger);

    private static Usuario CriarUsuario() =>
        new()
        {
            Id = Guid.NewGuid(),
            Nome = "Maria Souza",
            Email = "maria@empresa.com",
            AvatarUrl = null,
            TemaPreferido = "dark",
            Ativo = true,
            EmailConfirmado = true,
            CriadoEm = DateTime.UtcNow.AddYears(-1),
            AlteradoEm = DateTime.UtcNow,
            UltimoAcessoEm = DateTime.UtcNow.AddHours(-2)
        };

    [Fact]
    public async Task DeveLancarUsuarioNaoAutorizado_QuandoCurrentUserVazio()
    {
        _currentUser.UsuarioId.Returns(Guid.Empty);

        var useCase = CriarUseCase();
        var act = () => useCase.ExecuteAsync();

        await act.Should().ThrowAsync<UsuarioNaoAutorizadoException>();
    }

    [Fact]
    public async Task DeveLancarRegraDeDominio_QuandoUsuarioNaoExiste()
    {
        var usuarioId = Guid.NewGuid();
        _currentUser.UsuarioId.Returns(usuarioId);
        _usuarioRepository.GetByIdAsync(usuarioId).Returns((Usuario?)null);

        var useCase = CriarUseCase();
        var act = () => useCase.ExecuteAsync();

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task DeveDevolverSnapshotPiiCompleto_QuandoUsuarioExiste()
    {
        var usuario = CriarUsuario();
        var empresaId = Guid.NewGuid();
        _currentUser.UsuarioId.Returns(usuario.Id);
        _usuarioRepository.GetByIdAsync(usuario.Id).Returns(usuario);
        _usuarioEmpresaRepository.GetByUsuarioIdAsync(usuario.Id).Returns(new[]
        {
            new UsuarioEmpresa
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                EmpresaId = empresaId,
                Empresa = new Empresa { Id = empresaId, Nome = "Casa da Baba" }
            }
        });
        _refreshTokenRepository.GetByUsuarioIdAsync(usuario.Id).Returns(Array.Empty<RefreshToken>());

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync();

        result.Usuario.Id.Should().Be(usuario.Id);
        result.Usuario.Nome.Should().Be("Maria Souza");
        result.Usuario.Email.Should().Be("maria@empresa.com");
        result.Usuario.TemaPreferido.Should().Be("dark");
        result.Empresas.Should().HaveCount(1);
        result.Empresas.Single().Nome.Should().Be("Casa da Baba");
        result.GeradoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DeveFiltrarRefreshTokensExpiradosOuRevogados()
    {
        var usuario = CriarUsuario();
        _currentUser.UsuarioId.Returns(usuario.Id);
        _usuarioRepository.GetByIdAsync(usuario.Id).Returns(usuario);
        _usuarioEmpresaRepository.GetByUsuarioIdAsync(usuario.Id).Returns(Array.Empty<UsuarioEmpresa>());

        var agora = DateTime.UtcNow;
        _refreshTokenRepository.GetByUsuarioIdAsync(usuario.Id).Returns(new[]
        {
            // Ativo
            new RefreshToken { Id = Guid.NewGuid(), UsuarioId = usuario.Id, TokenHash = "h1",
                CriadoEm = agora.AddHours(-1), ExpiraEm = agora.AddDays(7) },
            // Expirado
            new RefreshToken { Id = Guid.NewGuid(), UsuarioId = usuario.Id, TokenHash = "h2",
                CriadoEm = agora.AddDays(-30), ExpiraEm = agora.AddDays(-1) },
            // Revogado
            new RefreshToken { Id = Guid.NewGuid(), UsuarioId = usuario.Id, TokenHash = "h3",
                CriadoEm = agora.AddHours(-2), ExpiraEm = agora.AddDays(7),
                RevogadoEm = agora.AddMinutes(-10) }
        });

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync();

        result.RefreshTokensAtivos.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExportaTelefoneVerificacaoEConsentimentos()
    {
        var usuario = CriarUsuario();
        usuario.DefinirTelefone(TelefoneE164.From("11997573992"));
        var verificadoEm = new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);
        usuario.MarcarTelefoneVerificado(verificadoEm);
        usuario.SolicitarTrocaDeEmail("nova@empresa.com");
        var empresaId = Guid.NewGuid();
        _currentUser.UsuarioId.Returns(usuario.Id);
        _usuarioRepository.GetByIdAsync(usuario.Id).Returns(usuario);
        _usuarioEmpresaRepository.GetByUsuarioIdAsync(usuario.Id).Returns(Array.Empty<UsuarioEmpresa>());
        _refreshTokenRepository.GetByUsuarioIdAsync(usuario.Id).Returns(Array.Empty<RefreshToken>());
        _consentimentos.ListarPorUsuarioAsync(usuario.Id).Returns(
        [
            ConsentimentoNotificacao.Registrar(
                usuario.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca, true, "superadmin:x", "10.0.0.1")
        ]);
        _preferencias.ListarPorUsuarioAsync(usuario.Id).Returns(
        [
            PreferenciaNotificacaoUsuario.Criar(usuario.Id, empresaId, "prazo_estourado_global", habilitada: false)
        ]);

        var result = await CriarUseCase().ExecuteAsync();

        result.Usuario.Telefone.Should().Be("+5511997573992");
        result.Usuario.TelefoneVerificadoEm.Should().Be(verificadoEm);
        result.Usuario.EmailPendente.Should().Be("nova@empresa.com");
        result.Consentimentos.Should().ContainSingle().Which.Should().Match<ConsentimentoExport>(c =>
            c.Canal == "WhatsApp" && c.Categoria == "Seguranca" && c.OptIn && c.IpOrigem == "10.0.0.1");
        result.Preferencias.Should().ContainSingle().Which.Should().Match<PreferenciaExport>(p =>
            p.EmpresaId == empresaId && p.RotinaCodigo == "prazo_estourado_global" && !p.Habilitada);
    }
}
