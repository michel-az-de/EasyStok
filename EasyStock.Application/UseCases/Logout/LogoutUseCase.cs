using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.Logout;

public sealed class LogoutUseCase(
    IRefreshTokenRepository refreshTokenRepository,
    IUsuarioRepository usuarioRepository,
    RevogadorSessoes revogadorSessoes,
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

        // #1879: o access token já emitido valia até o exp (8 h) depois do "Sair". Gravar o corte de sessão do
        // usuário (#1352) derruba na hora todo JWT anterior, inclusive o do SSE; quem já saiu, ou nunca teve
        // refresh válido, não chega aqui. Antes do commit, como o próprio serviço pede.
        var usuario = await usuarioRepository.GetByIdAsync(refreshToken.UsuarioId);
        if (usuario is not null)
            await revogadorSessoes.RevogarAsync(usuario);

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