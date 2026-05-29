using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Logout;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class LogoutUseCaseTests
{
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<LogoutUseCase> _logger = Substitute.For<ILogger<LogoutUseCase>>();

    private LogoutUseCase CriarUseCase() =>
        new(_refreshTokenRepository, _auditLogRepository, _unitOfWork, _logger);

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
}
