using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.EsqueciSenha;

public sealed class EsqueciSenhaUseCase(
    IUsuarioRepository usuarioRepository,
    IResetTokenRepository resetTokenRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ILogger<EsqueciSenhaUseCase> logger) : IUseCase<EsqueciSenhaCommand, EsqueciSenhaResult>
{
    public async Task<EsqueciSenhaResult> ExecuteAsync(EsqueciSenhaCommand command)
    {
        logger.LogInformation("Iniciando esqueci senha para email {Email}", command.Email);

        var usuario = await usuarioRepository.GetByEmailAsync(command.Email);
        if (usuario == null || !usuario.Ativo)
        {
            logger.LogWarning("Tentativa de esqueci senha para email inexistente: {Email}", command.Email);
            // Retornar sucesso para nao revelar se email existe
            return new EsqueciSenhaResult(true);
        }

        // Gerar token
        var token = Guid.NewGuid().ToString();
        var expiraEm = DateTime.UtcNow.AddHours(1); // 1 hora
        var resetToken = ResetToken.Criar(
            usuario.Id,
            token,
            expiraEm,
            null,
            null);
        await resetTokenRepository.AddAsync(resetToken);

        // Auditar
        var auditLog = AuditLog.Criar(
            usuario.Id,
            "forgot-password",
            true,
            "Token de reset enviado",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        await unitOfWork.CommitAsync();

        // TODO: Enviar email com token
        logger.LogInformation("Token de reset gerado para usuario {UsuarioId}", usuario.Id);

        return new EsqueciSenhaResult(true);
    }
}