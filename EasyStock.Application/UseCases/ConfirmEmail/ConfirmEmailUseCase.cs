using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ConfirmEmail;

public sealed class ConfirmEmailUseCase(
    IEmailConfirmationTokenRepository emailTokenRepository,
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ILogger<ConfirmEmailUseCase> logger) : IUseCase<ConfirmEmailCommand, ConfirmEmailResult>
{
    public async Task<ConfirmEmailResult> ExecuteAsync(ConfirmEmailCommand command)
    {
        logger.LogInformation("Iniciando confirmação de email com token");

        var emailToken = await emailTokenRepository.GetByTokenAsync(command.Token);
        if (emailToken == null || !emailToken.EstaValido())
        {
            logger.LogWarning("Token de confirmação inválido ou expirado");
            throw new RegraDeDominioVioladaException("Token inválido ou expirado.");
        }

        var usuario = await usuarioRepository.GetByIdAsync(emailToken.UsuarioId);
        if (usuario == null)
        {
            logger.LogWarning("Usuário não encontrado: {UsuarioId}", emailToken.UsuarioId);
            throw new RegraDeDominioVioladaException("Usuário não encontrado.");
        }

        usuario.EmailConfirmado = true;
        usuario.AlteradoEm = DateTime.UtcNow;
        await usuarioRepository.UpdateAsync(usuario);

        emailToken.MarcarComoConfirmado();
        await emailTokenRepository.UpdateAsync(emailToken);

        var auditLog = AuditLog.Criar(
            usuario.Id,
            "email-confirmado",
            true,
            "Email confirmado com sucesso",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        await unitOfWork.CommitAsync();

        logger.LogInformation("Email confirmado com sucesso para usuário {UsuarioId}", usuario.Id);
        return new ConfirmEmailResult(true, "Email confirmado com sucesso!");
    }
}
