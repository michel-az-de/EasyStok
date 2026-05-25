using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ExportarMeusDados;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class ExportarMeusDadosUseCaseTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IUsuarioEmpresaRepository _usuarioEmpresaRepository = Substitute.For<IUsuarioEmpresaRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ILogger<ExportarMeusDadosUseCase> _logger = Substitute.For<ILogger<ExportarMeusDadosUseCase>>();

    private ExportarMeusDadosUseCase CriarUseCase() =>
        new(_usuarioRepository, _usuarioEmpresaRepository, _refreshTokenRepository, _currentUser, _logger);

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
}
