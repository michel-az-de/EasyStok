using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;
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

        // Verificar se refresh token existe e e valido
        var tokenHash = BCrypt.Net.BCrypt.HashPassword(command.RefreshToken);
        var refreshToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash);
        if (refreshToken == null || !refreshToken.EstaValido())
        {
            logger.LogWarning("Refresh token invalido ou expirado");
            throw new CredenciaisInvalidasException("Refresh token invalido.");
        }

        // Obter usuario
        var usuario = await usuarioRepository.GetByIdAsync(refreshToken.UsuarioId);
        if (usuario == null || !usuario.Ativo)
        {
            logger.LogWarning("Usuario nao encontrado ou inativo: {UsuarioId}", refreshToken.UsuarioId);
            throw new CredenciaisInvalidasException("Usuario inativo.");
        }

        // Revogar token antigo
        refreshToken.Revogar();
        await refreshTokenRepository.UpdateAsync(refreshToken);

        // Criar novo refresh token
        var novoRefreshTokenValue = Guid.NewGuid().ToString();
        var novoTokenHash = BCrypt.Net.BCrypt.HashPassword(novoRefreshTokenValue);
        var expiraEm = DateTime.UtcNow.AddDays(7); // 7 dias
        var novoRefreshToken = RefreshTokenEntity.Criar(
            usuario.Id,
            novoTokenHash,
            expiraEm,
            null, // IP
            null); // UserAgent
        await refreshTokenRepository.AddAsync(novoRefreshToken);

        // Gerar novo access token
        var nivel = NivelAcesso.Visualizador;
        IReadOnlyCollection<Permissao> permissoes = [];
        if (usuario.Perfis is not null && usuario.Perfis.Any())
        {
            var perfil = usuario.Perfis.OrderBy(p => p.Perfil != null ? (int)p.Perfil.Nivel : int.MaxValue).FirstOrDefault();
            if (perfil?.Perfil is not null)
            {
                nivel = perfil.Perfil.Nivel;
                permissoes = perfil.Perfil.Permissoes?.Select(p => p.Permissao).Distinct().ToArray() ?? [];
            }
        }

        var autenticarResult = new AutenticarUsuarioResult(
            usuario.Id,
            null, // EmpresaId
            usuario.Nome,
            usuario.Email,
            nivel,
            permissoes);
        var accessToken = jwtTokenService.GerarToken(autenticarResult);

        // Auditar
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