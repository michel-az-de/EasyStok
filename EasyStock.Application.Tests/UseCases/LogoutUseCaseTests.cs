using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class LogoutUseCaseTests
{
    private static LogoutUseCase CriarUseCase(
        IRefreshTokenRepository refreshTokenRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork)
    {
        var logger = Substitute.For<ILogger<LogoutUseCase>>();
        return new LogoutUseCase(refreshTokenRepository, auditLogRepository, unitOfWork, logger);
    }

    [Fact]
    public async Task Logout_DeveRevogarToken_QuandoTokenValido()
    {
        var tokenValue = Guid.NewGuid().ToString();
        var tokenHash = TokenHashHelper.ComputeSha256(tokenValue);
        var usuarioId = Guid.NewGuid();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TokenHash = tokenHash,
            CriadoEm = DateTime.UtcNow,
            ExpiraEm = DateTime.UtcNow.AddDays(7),
            Revogado = false
        };

        var refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
        var auditLogRepository = Substitute.For<IAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);

        var useCase = CriarUseCase(refreshTokenRepository, auditLogRepository, unitOfWork);
        var result = await useCase.ExecuteAsync(new LogoutCommand(tokenValue));

        Assert.True(result.Success);
        Assert.True(refreshToken.Revogado);
        await refreshTokenRepository.Received(1).UpdateAsync(refreshToken);
        await auditLogRepository.Received(1).AddAsync(Arg.Any<AuditLog>());
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Logout_DeveRetornarFalse_QuandoTokenNaoEncontrado()
    {
        var refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
        var auditLogRepository = Substitute.For<IAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        refreshTokenRepository.GetByTokenHashAsync(Arg.Any<string>()).Returns((RefreshToken?)null);

        var useCase = CriarUseCase(refreshTokenRepository, auditLogRepository, unitOfWork);
        var result = await useCase.ExecuteAsync(new LogoutCommand(Guid.NewGuid().ToString()));

        Assert.False(result.Success);
        await refreshTokenRepository.DidNotReceive().UpdateAsync(Arg.Any<RefreshToken>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Logout_DeveRetornarFalse_QuandoTokenRevogado()
    {
        var tokenValue = Guid.NewGuid().ToString();
        var tokenHash = TokenHashHelper.ComputeSha256(tokenValue);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = Guid.NewGuid(),
            TokenHash = tokenHash,
            CriadoEm = DateTime.UtcNow,
            ExpiraEm = DateTime.UtcNow.AddDays(7),
            Revogado = true,
            RevogadoEm = DateTime.UtcNow.AddMinutes(-5)
        };

        var refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
        var auditLogRepository = Substitute.For<IAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);

        var useCase = CriarUseCase(refreshTokenRepository, auditLogRepository, unitOfWork);
        var result = await useCase.ExecuteAsync(new LogoutCommand(tokenValue));

        Assert.False(result.Success);
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Logout_DeveRetornarFalse_QuandoTokenExpirado()
    {
        var tokenValue = Guid.NewGuid().ToString();
        var tokenHash = TokenHashHelper.ComputeSha256(tokenValue);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = Guid.NewGuid(),
            TokenHash = tokenHash,
            CriadoEm = DateTime.UtcNow.AddDays(-8),
            ExpiraEm = DateTime.UtcNow.AddDays(-1),
            Revogado = false
        };

        var refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
        var auditLogRepository = Substitute.For<IAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);

        var useCase = CriarUseCase(refreshTokenRepository, auditLogRepository, unitOfWork);
        var result = await useCase.ExecuteAsync(new LogoutCommand(tokenValue));

        Assert.False(result.Success);
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Logout_DeveCriarAuditLog_QuandoLogoutBemSucedido()
    {
        var tokenValue = Guid.NewGuid().ToString();
        var tokenHash = TokenHashHelper.ComputeSha256(tokenValue);
        var usuarioId = Guid.NewGuid();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TokenHash = tokenHash,
            CriadoEm = DateTime.UtcNow,
            ExpiraEm = DateTime.UtcNow.AddDays(7),
            Revogado = false
        };

        var refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
        var auditLogRepository = Substitute.For<IAuditLogRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        refreshTokenRepository.GetByTokenHashAsync(tokenHash).Returns(refreshToken);

        var useCase = CriarUseCase(refreshTokenRepository, auditLogRepository, unitOfWork);
        await useCase.ExecuteAsync(new LogoutCommand(tokenValue));

        await auditLogRepository.Received(1).AddAsync(
            Arg.Is<AuditLog>(a => a.UsuarioId == usuarioId && a.Acao == "logout"));
    }
}
