using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Logout;

public sealed class LogoutUseCase(
    IRefreshTokenRepository refreshTokenRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ILogger<LogoutUseCase> logger) : IUseCase<LogoutCommand, LogoutResult>
{
    public async Task<LogoutResult> ExecuteAsync(LogoutCommand command)
    {
        logger.LogInformation("Iniciando logout");

        var tokenHash = TokenHashHelper.ComputeSha256Hash(command.RefreshToken);
        var refreshToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash);
        if (refreshToken == null || !refreshToken.EstaValido())
        {
            logger.LogWarning("Tentativa de logout com token invalido");
            return new LogoutResult(false);
        }

        refreshToken.Revogar();
        await refreshTokenRepository.UpdateAsync(refreshToken);

        var auditLog = AuditLog.Criar(
            refreshToken.UsuarioId,
            "logout",
            true,
            "Logout realizado com sucesso",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        await unitOfWork.CommitAsync();

        logger.LogInformation("Logout realizado com sucesso para usuario {UsuarioId}", refreshToken.UsuarioId);
        return new LogoutResult(true);
    }
}
