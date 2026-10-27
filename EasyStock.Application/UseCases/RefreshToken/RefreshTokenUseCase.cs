using EasyStock.Application.Services.Auth;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;
using RefreshTokenEntity = EasyStock.Domain.Entities.RefreshToken;

namespace EasyStock.Application.UseCases.RefreshToken;

public sealed class RefreshTokenUseCase(
    IRefreshTokenRepository refreshTokenRepository,
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    IJwtTokenService jwtTokenService,
    IUnitOfWork unitOfWork,
    ILogger<RefreshTokenUseCase> logger) : IUseCase<RefreshTokenCommand, RefreshTokenResult>
{
    public async Task<RefreshTokenResult> ExecuteAsync(RefreshTokenCommand command)
    {
        logger.LogInformation("Iniciando refresh token");

        var tokenHash = TokenHashHelper.ComputeSha256Hash(command.RefreshToken);
        var refreshToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash);
        if (refreshToken == null || !refreshToken.EstaValido())
        {
            logger.LogWarning("Refresh token invalido ou expirado");
            throw new CredenciaisInvalidasException("Refresh token invalido.");
        }

        var usuario = await usuarioRepository.GetByIdAsync(refreshToken.UsuarioId);
        if (usuario == null || !usuario.Ativo)
        {
            logger.LogWarning("Usuario nao encontrado ou inativo: {UsuarioId}", refreshToken.UsuarioId);
            throw new CredenciaisInvalidasException("Usuario inativo.");
        }

        // M3 (#2070): a regra do login, na empresa da sessão. Token antigo (sem a coluna) segue a regra de antes: a única
        // empresa ativa. Sem vínculo ativo com a empresa da sessão, recusa antes de revogar ou emitir qualquer coisa.
        var empresaDaSessao = refreshToken.EmpresaDaSessaoId;
        var autenticarResult = ResolvedorDeSessao.Resolver(
            usuario, empresaDaSessao ?? ResolvedorDeSessao.EmpresaPadrao(usuario), empresaDoSuporte: empresaDaSessao);

        refreshToken.Revogar();
        await refreshTokenRepository.UpdateAsync(refreshToken);

        var novoRefreshTokenValue = Guid.NewGuid().ToString();
        var novoTokenHash = TokenHashHelper.ComputeSha256Hash(novoRefreshTokenValue);
        var expiraEm = DateTime.UtcNow.AddDays(7);
        var novoRefreshToken = RefreshTokenEntity.Criar(
            usuario.Id,
            novoTokenHash,
            expiraEm,
            null,
            null,
            autenticarResult.EmpresaId);
        await refreshTokenRepository.AddAsync(novoRefreshToken);

        var accessToken = jwtTokenService.GerarToken(autenticarResult);

        var auditLog = AuditLogEntity.Criar(
            usuario.Id,
            "refresh",
            true,
            "Token renovado com sucesso",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        await unitOfWork.CommitAsync();

        logger.LogInformation("Refresh token executado com sucesso para usuario {UsuarioId}", usuario.Id);
        return new RefreshTokenResult(accessToken, novoRefreshTokenValue, jwtTokenService.ExpiresInSeconds);
    }
}
