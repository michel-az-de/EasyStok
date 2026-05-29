namespace EasyStock.Application.UseCases.EsqueciSenha;

public sealed class EsqueciSenhaUseCase(
    IUsuarioRepository usuarioRepository,
    IResetTokenRepository resetTokenRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ILogger<EsqueciSenhaUseCase> logger,
    IEmailService? emailService = null) : IUseCase<EsqueciSenhaCommand, EsqueciSenhaResult>
{
    public async Task<EsqueciSenhaResult> ExecuteAsync(EsqueciSenhaCommand command)
    {
        logger.LogInformation("Iniciando esqueci senha para email {Email}", command.Email);

        // Formato inválido é tratado da mesma forma que email inexistente:
        // retorna sucesso sem efeito colateral, para não vazar informação
        // sobre formato aceito vs contas cadastradas (mesma classe de leak
        // que user enumeration).
        if (!EmailValidator.IsValid(command.Email))
        {
            logger.LogWarning("Tentativa de esqueci senha com email em formato invalido.");
            return new EsqueciSenhaResult(true);
        }

        var usuario = await usuarioRepository.GetByEmailAsync(command.Email);
        if (usuario == null || !usuario.Ativo)
        {
            logger.LogWarning("Tentativa de esqueci senha para email inexistente: {Email}", command.Email);
            // Retornar sucesso para nao revelar se email existe
            return new EsqueciSenhaResult(true);
        }

        // Token plaintext só circula em memória/email; persistimos só o hash.
        var token = Guid.NewGuid().ToString();
        var tokenHash = TokenHashHelper.ComputeSha256Hash(token);
        var expiraEm = DateTime.UtcNow.AddHours(1);
        var resetToken = ResetToken.Criar(
            usuario.Id,
            tokenHash,
            expiraEm,
            null,
            null);
        await resetTokenRepository.AddAsync(resetToken);

        var auditLog = AuditLog.Criar(
            usuario.Id,
            "forgot-password",
            true,
            "Token de reset enviado",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        await unitOfWork.CommitAsync();

        if (emailService is not null)
        {
            try
            {
                var resetLink = !string.IsNullOrEmpty(command.BaseUrl)
                    ? $"{command.BaseUrl.TrimEnd('/')}/auth/redefinir-senha?token={Uri.EscapeDataString(token)}"
                    : token;

                var hasLink = !string.IsNullOrEmpty(command.BaseUrl);
                var subject = "Recuperação de senha - EasyStock";
                var body = $"Olá {usuario.Nome},\n\n" +
                           $"Recebemos uma solicitação para redefinir a senha da sua conta.\n\n" +
                           (hasLink
                               ? $"Clique no link abaixo para criar uma nova senha (válido por 1 hora):\n\n{resetLink}\n\n"
                               : $"Use o token abaixo para criar uma nova senha (válido por 1 hora):\n\n{token}\n\n") +
                           $"Se você não solicitou a redefinição de senha, ignore este e-mail.\n\n" +
                           $"Equipe EasyStock";

                await emailService.SendAsync(usuario.Email, subject, body);
                logger.LogInformation("E-mail de recuperacao de senha enviado para {Email}", usuario.Email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao enviar e-mail de recuperacao de senha para {Email}. Token gerado normalmente.", usuario.Email);
            }
        }
        else
        {
            logger.LogInformation("Token de reset gerado para usuario {UsuarioId}", usuario.Id);
        }

        return new EsqueciSenhaResult(true);
    }
}