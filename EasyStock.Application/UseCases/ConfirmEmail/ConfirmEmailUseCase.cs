using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.ConfirmEmail;

public sealed class ConfirmEmailUseCase(
    IEmailConfirmationTokenRepository emailTokenRepository,
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    RevogadorSessoes revogadorSessoes,
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

        // N4: com troca pendente, o clique confirma o endereco NOVO e so agora o Email da conta muda. O login segue
        // pelo endereco antigo ate aqui. A unicidade vale de novo: outro usuario pode ter tomado o endereco no intervalo.
        var trocaDeEmail = !string.IsNullOrWhiteSpace(usuario.EmailPendente);
        if (trocaDeEmail)
        {
            var dono = await usuarioRepository.GetByEmailAsync(usuario.EmailPendente!);
            if (dono is not null && dono.Id != usuario.Id)
            {
                logger.LogWarning("Troca de e-mail do usuario {UsuarioId} recusada: endereco ja usado por outra conta", usuario.Id);
                throw new RegraDeDominioVioladaException("Email ja cadastrado.");
            }

            usuario.ConfirmarNovoEmail();
        }
        else
        {
            usuario.EmailConfirmado = true;
        }

        usuario.AlteradoEm = DateTime.UtcNow;
        await usuarioRepository.UpdateAsync(usuario);

        // N7: e-mail da conta trocado derruba as sessoes (antes do commit: o pior caso e deslogar sem a troca).
        if (trocaDeEmail)
            await revogadorSessoes.RevogarAsync(usuario);

        emailToken.MarcarComoConfirmado();
        await emailTokenRepository.UpdateAsync(emailToken);

        var auditLog = AuditLog.Criar(
            usuario.Id,
            trocaDeEmail ? "email-alterado" : "email-confirmado",
            true,
            trocaDeEmail ? "Novo email confirmado e aplicado a conta" : "Email confirmado com sucesso",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        await unitOfWork.CommitAsync();

        logger.LogInformation("Email confirmado com sucesso para usuário {UsuarioId}", usuario.Id);
        return new ConfirmEmailResult(true, "Email confirmado com sucesso!");
    }
}
