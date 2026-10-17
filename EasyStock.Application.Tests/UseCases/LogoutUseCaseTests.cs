using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.Logout;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class LogoutUseCaseTests
{
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<LogoutUseCase> _logger = Substitute.For<ILogger<LogoutUseCase>>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private static readonly DateTimeOffset Agora = new(2026, 10, 17, 14, 30, 5, 321, TimeSpan.Zero);
    private static readonly DateTime CorteEsperado = new(2026, 10, 17, 14, 30, 5, DateTimeKind.Utc);

    private LogoutUseCase CriarUseCase() =>
        new(_refreshTokenRepository, _usuarios,
            new RevogadorSessoes(_usuarios, _refreshTokenRepository, _cache, new FakeTimeProvider(Agora),
                Substitute.For<ILogger<RevogadorSessoes>>()),
            _auditLogRepository, _unitOfWork, _logger);

    [Fact]
    public async Task Logout_DeveRevogarToken_QuandoTokenValido()
    {
        var usuarioId = Guid.NewGuid();
        var rawToken = "token-valido-123";
        var tokenHash = TokenHashHelper.ComputeSha256Hash(rawToken);

        var refreshToken = RefreshToken.Criar(usuarioId, tokenHash, DateTime.UtcNow.AddDays(7), null, null);

        _refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new LogoutCommand(rawToken));

        Assert.True(result.Success);
        Assert.True(refreshToken.Revogado);
        await _refreshTokenRepository.Received(1).UpdateAsync(refreshToken);
        await _auditLogRepository.Received(1).AddAsync(Arg.Any<AuditLog>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Logout_DeveRetornarFalse_QuandoTokenNaoEncontrado()
    {
        _refreshTokenRepository.GetByTokenHashAsync(Arg.Any<string>()).Returns((RefreshToken?)null);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new LogoutCommand("token-inexistente"));

        Assert.False(result.Success);
        await _refreshTokenRepository.DidNotReceive().UpdateAsync(Arg.Any<RefreshToken>());
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Logout_DeveRetornarFalse_QuandoTokenRevogado()
    {
        var usuarioId = Guid.NewGuid();
        var rawToken = "token-revogado-456";
        var tokenHash = TokenHashHelper.ComputeSha256Hash(rawToken);

        var refreshToken = RefreshToken.Criar(usuarioId, tokenHash, DateTime.UtcNow.AddDays(7), null, null);
        refreshToken.Revogar();

        _refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new LogoutCommand(rawToken));

        Assert.False(result.Success);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Logout_DeveRetornarFalse_QuandoTokenExpirado()
    {
        var usuarioId = Guid.NewGuid();
        var rawToken = "token-expirado-789";
        var tokenHash = TokenHashHelper.ComputeSha256Hash(rawToken);

        var refreshToken = RefreshToken.Criar(usuarioId, tokenHash, DateTime.UtcNow.AddDays(-1), null, null);

        _refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new LogoutCommand(rawToken));

        Assert.False(result.Success);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Logout_DeveRegistrarAuditLog_QuandoLogoutBemSucedido()
    {
        var usuarioId = Guid.NewGuid();
        var rawToken = "token-auditado-abc";
        var tokenHash = TokenHashHelper.ComputeSha256Hash(rawToken);

        var refreshToken = RefreshToken.Criar(usuarioId, tokenHash, DateTime.UtcNow.AddDays(7), null, null);
        _refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);

        var useCase = CriarUseCase();
        await useCase.ExecuteAsync(new LogoutCommand(rawToken));

        await _auditLogRepository.Received(1).AddAsync(Arg.Is<AuditLog>(a =>
            a.UsuarioId == usuarioId &&
            a.Acao == "logout" &&
            a.Sucesso == true));
    }

    // ── #1879: o logout derruba também os JWT já emitidos (corte de sessão do usuário) ───────────────

    [Fact]
    public async Task Logout_DeveGravarOCorteDeSessao_ParaOJwtAnteriorNaoValerMais()
    {
        var usuario = Usuario.Criar("Ana", "ana@casadababa.com", "hash");
        var rawToken = "token-do-jwt-vivo";
        var tokenHash = TokenHashHelper.ComputeSha256Hash(rawToken);
        var refreshToken = RefreshToken.Criar(usuario.Id, tokenHash, DateTime.UtcNow.AddDays(7), null, null);
        _refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);
        _usuarios.GetByIdAsync(usuario.Id).Returns(usuario);

        var result = await CriarUseCase().ExecuteAsync(new LogoutCommand(rawToken));

        Assert.True(result.Success);
        await _usuarios.Received(1).AtualizarSessoesValidasDesdeAsync(usuario.Id, CorteEsperado);
        await _refreshTokenRepository.Received(1).RevogarSessoesAtivasAsync(usuario.Id, Agora.UtcDateTime);
        await _cache.Received(1).RemoveAsync(CacheKeys.Sessao(usuario.Id));
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Logout_NaoGravaCorte_QuandoORefreshNaoValeOuNaoExiste()
    {
        var usuarioId = Guid.NewGuid();
        var revogado = RefreshToken.Criar(usuarioId, TokenHashHelper.ComputeSha256Hash("revogado"), DateTime.UtcNow.AddDays(7), null, null);
        revogado.Revogar();
        _refreshTokenRepository.GetByTokenHashAsync(TokenHashHelper.ComputeSha256Hash("revogado")).Returns(revogado);
        _refreshTokenRepository.GetByTokenHashAsync(TokenHashHelper.ComputeSha256Hash("nao-existe")).Returns((RefreshToken?)null);

        foreach (var token in new[] { "revogado", "nao-existe" })
            Assert.False((await CriarUseCase().ExecuteAsync(new LogoutCommand(token))).Success);

        await _usuarios.DidNotReceiveWithAnyArgs().AtualizarSessoesValidasDesdeAsync(default, default);
        await _refreshTokenRepository.DidNotReceiveWithAnyArgs().RevogarSessoesAtivasAsync(default, default);
    }

    [Fact]
    public async Task Logout_RevogaORefresh_SemCorte_QuandoOUsuarioNaoExisteMais()
    {
        var usuarioId = Guid.NewGuid();
        var tokenHash = TokenHashHelper.ComputeSha256Hash("conta-apagada");
        var refreshToken = RefreshToken.Criar(usuarioId, tokenHash, DateTime.UtcNow.AddDays(7), null, null);
        _refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);
        _usuarios.GetByIdAsync(usuarioId).Returns((Usuario?)null);

        var result = await CriarUseCase().ExecuteAsync(new LogoutCommand("conta-apagada"));

        Assert.True(result.Success);
        Assert.True(refreshToken.Revogado);
        await _usuarios.DidNotReceiveWithAnyArgs().AtualizarSessoesValidasDesdeAsync(default, default);
        await _unitOfWork.Received(1).CommitAsync();
    }
}
