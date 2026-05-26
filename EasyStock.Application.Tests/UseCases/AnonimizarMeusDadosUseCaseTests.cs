using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.TestHelpers;
using EasyStock.Application.UseCases.AnonimizarMeusDados;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class AnonimizarMeusDadosUseCaseTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IResetTokenRepository _resetTokenRepository = Substitute.For<IResetTokenRepository>();
    private readonly IEmailConfirmationTokenRepository _emailConfirmationTokenRepository = Substitute.For<IEmailConfirmationTokenRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<AnonimizarMeusDadosUseCase> _logger = Substitute.For<ILogger<AnonimizarMeusDadosUseCase>>();

    private AnonimizarMeusDadosUseCase CriarUseCase() =>
        new(_usuarioRepository, _refreshTokenRepository, _resetTokenRepository,
            _emailConfirmationTokenRepository, _currentUser, _unitOfWork, _logger);

    private static Usuario CriarUsuario() =>
        new()
        {
            Id = Guid.NewGuid(),
            Nome = "Joao Silva",
            Email = "joao@empresa.com",
            AvatarUrl = "https://cdn/avatar.png",
            SenhaHash = FakePasswordHasher.MakeHash("Senha@123"),
            Ativo = true,
            EmailConfirmado = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

    [Fact]
    public async Task DeveLancarUsuarioNaoAutorizado_QuandoCurrentUserVazio()
    {
        _currentUser.UsuarioId.Returns(Guid.Empty);

        var useCase = CriarUseCase();
        var act = () => useCase.ExecuteAsync(new AnonimizarMeusDadosCommand("ANONIMIZAR"));

        await act.Should().ThrowAsync<UsuarioNaoAutorizadoException>();
        await _usuarioRepository.DidNotReceive().UpdateAsync(Arg.Any<Usuario>());
    }

    [Fact]
    public async Task DeveLancarUseCaseValidation_QuandoConfirmacaoIncorreta()
    {
        _currentUser.UsuarioId.Returns(Guid.NewGuid());

        var useCase = CriarUseCase();
        var act = () => useCase.ExecuteAsync(new AnonimizarMeusDadosCommand("anonimizar"));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*ANONIMIZAR*");
        await _usuarioRepository.DidNotReceive().UpdateAsync(Arg.Any<Usuario>());
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarRegraDeDominio_QuandoUsuarioNaoExiste()
    {
        var usuarioId = Guid.NewGuid();
        _currentUser.UsuarioId.Returns(usuarioId);
        _usuarioRepository.GetByIdAsync(usuarioId).Returns((Usuario?)null);

        var useCase = CriarUseCase();
        var act = () => useCase.ExecuteAsync(new AnonimizarMeusDadosCommand("ANONIMIZAR"));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task DeveAnonimizarUsuarioERemoverCredenciais_QuandoSucesso()
    {
        var usuario = CriarUsuario();
        _currentUser.UsuarioId.Returns(usuario.Id);
        _usuarioRepository.GetByIdAsync(usuario.Id).Returns(usuario);
        _refreshTokenRepository.DeleteAllByUsuarioIdAsync(usuario.Id).Returns(2);
        _resetTokenRepository.DeleteAllByUsuarioIdAsync(usuario.Id).Returns(1);
        _emailConfirmationTokenRepository.DeleteAllByUsuarioIdAsync(usuario.Id).Returns(0);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new AnonimizarMeusDadosCommand("ANONIMIZAR"));

        // Usuario anonimizado em campos PII
        usuario.Nome.Should().Be("[Anonimizado]");
        usuario.Email.Should().StartWith("anonimizado-").And.EndWith("@anonimizado.local");
        usuario.AvatarUrl.Should().BeNull();
        // SenhaHash recebe um marcador bcrypt invalido ($2a$10$INVALIDATED_*)
        // que e impossivel de bater com qualquer senha em BCrypt.Verify.
        // Anteriormente era string.Empty — inseguro porque caminhos que
        // comparavam literal podiam aceitar entrada em branco.
        usuario.SenhaHash.Should().StartWith("$2a$10$INVALIDATED_");
        usuario.Ativo.Should().BeFalse();
        usuario.EmailConfirmado.Should().BeFalse();

        // Id preservado (FKs em audit/movimentacoes ficam validas)
        usuario.Id.Should().NotBeEmpty();

        // Cascata de credenciais
        await _refreshTokenRepository.Received(1).DeleteAllByUsuarioIdAsync(usuario.Id);
        await _resetTokenRepository.Received(1).DeleteAllByUsuarioIdAsync(usuario.Id);
        await _emailConfirmationTokenRepository.Received(1).DeleteAllByUsuarioIdAsync(usuario.Id);

        // Commit + update
        await _usuarioRepository.Received(1).UpdateAsync(usuario);
        await _unitOfWork.Received(1).CommitAsync();

        // Result reflete contagem
        result.UsuarioId.Should().Be(usuario.Id);
        result.RefreshTokensRemovidos.Should().Be(2);
        result.ResetTokensRemovidos.Should().Be(1);
        result.EmailConfirmationTokensRemovidos.Should().Be(0);
    }

    [Fact]
    public async Task EmailAnonimizadoDeveSerUnicoPorUsuario()
    {
        var u1 = CriarUsuario();
        var u2 = CriarUsuario();
        u1.Anonimizar();
        u2.Anonimizar();

        u1.Email.Should().NotBe(u2.Email);
        u1.Email.Should().StartWith("anonimizado-").And.EndWith("@anonimizado.local");
        u2.Email.Should().StartWith("anonimizado-").And.EndWith("@anonimizado.local");
    }
}
