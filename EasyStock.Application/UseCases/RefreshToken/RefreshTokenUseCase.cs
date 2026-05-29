using EasyStock.Application.UseCases.AutenticarUsuario;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;
using RefreshTokenEntity = EasyStock.Domain.Entities.RefreshToken;
using UsuarioEntity = EasyStock.Domain.Entities.Usuario;

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
            null);
        await refreshTokenRepository.AddAsync(novoRefreshToken);

        var empresaId = ResolveEmpresaIdPadrao(usuario);

        var nivel = NivelAcesso.Visualizador;
        IReadOnlyCollection<Permissao> permissoes = [];

        if (empresaId.HasValue && usuario.Perfis is not null)
        {
            var perfil = usuario.Perfis
                .Where(p => p.EmpresaId == empresaId.Value)
                .OrderBy(p => p.Perfil != null ? (int)p.Perfil.Nivel : int.MaxValue)
                .FirstOrDefault();
            if (perfil?.Perfil is not null)
            {
                nivel = perfil.Perfil.Nivel;
                permissoes = perfil.Perfil.Permissoes?.Select(p => p.Permissao).Distinct().ToArray() ?? [];
            }
        }

        var autenticarResult = new AutenticarUsuarioResult(
            usuario.Id,
            empresaId,
            usuario.Nome,
            usuario.Email,
            nivel,
            permissoes);
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

    private static Guid? ResolveEmpresaIdPadrao(UsuarioEntity usuario)
    {
        var empresasAtivas = usuario.Empresas?
            .Where(e => e.Ativo)
            .Select(e => e.EmpresaId)
            .Distinct()
            .ToArray() ?? [];

        return empresasAtivas.Length == 1 ? empresasAtivas[0] : null;
    }
}