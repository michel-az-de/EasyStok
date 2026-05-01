using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ResetarSenha;

public sealed class ResetarSenhaUseCase(
    IResetTokenRepository resetTokenRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ILogger<ResetarSenhaUseCase> logger) : IUseCase<ResetarSenhaCommand, ResetarSenhaResult>
{
    public async Task<ResetarSenhaResult> ExecuteAsync(ResetarSenhaCommand command)
    {
        logger.LogInformation("Iniciando reset de senha");

        var resetToken = await resetTokenRepository.GetByTokenAsync(command.Token);
        if (resetToken == null || !resetToken.EstaValido())
        {
            logger.LogWarning("Token de reset invalido ou expirado");
            throw new RegraDeDominioVioladaException("Token invalido ou expirado.");
        }

        var usuario = await usuarioRepository.GetByIdAsync(resetToken.UsuarioId);
        if (usuario == null || !usuario.Ativo)
        {
            logger.LogWarning("Usuario nao encontrado ou inativo: {UsuarioId}", resetToken.UsuarioId);
            throw new RegraDeDominioVioladaException("Usuario invalido.");
        }

        usuario.SenhaHash = BCrypt.Net.BCrypt.HashPassword(command.NovaSenha);
        usuario.AlteradoEm = DateTime.UtcNow;
        usuario.ResetarTentativasFalha();
        await usuarioRepository.UpdateAsync(usuario);

        resetToken.MarcarComoUsado();
        await resetTokenRepository.UpdateAsync(resetToken);

        // Revogar todos os refresh tokens anteriores para invalidar sessões
        var refreshTokensAntigos = await refreshTokenRepository.GetByUsuarioIdAsync(usuario.Id);
        var tokensRevogados = 0;
        foreach (var token in refreshTokensAntigos.Where(t => t.EstaValido()))
        {
            token.Revogar();
            await refreshTokenRepository.UpdateAsync(token);
            tokensRevogados++;
        }

        var auditLog = AuditLog.Criar(
            usuario.Id,
            "reset-password",
            true,
            $"Senha resetada com sucesso. {tokensRevogados} refresh tokens revogados.",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        await unitOfWork.CommitAsync();

        logger.LogInformation("Senha resetada com sucesso para usuario {UsuarioId}", usuario.Id);
        return new ResetarSenhaResult(true);
    }
}