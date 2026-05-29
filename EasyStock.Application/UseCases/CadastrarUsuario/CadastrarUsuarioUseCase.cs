namespace EasyStock.Application.UseCases.CadastrarUsuario;

public sealed class CadastrarUsuarioUseCase(
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    IEmailConfirmationTokenRepository emailTokenRepository,
    IEmailService? emailService,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ILogger<CadastrarUsuarioUseCase> logger) : IUseCase<CadastrarUsuarioCommand, CadastrarUsuarioResult>
{
    public async Task<CadastrarUsuarioResult> ExecuteAsync(CadastrarUsuarioCommand command)
    {
        EmailValidator.EnsureValid(command.Email);

        logger.LogInformation("Iniciando cadastro de usuario com email {Email}", command.Email);

        var usuarioExistente = await usuarioRepository.GetByEmailAsync(command.Email);
        if (usuarioExistente != null)
        {
            logger.LogWarning("Tentativa de cadastro com email ja existente: {Email}", command.Email);
            throw new RegraDeDominioVioladaException("Email ja cadastrado.");
        }

        var senhaHash = passwordHasher.Hash(command.Senha);
        var usuario = Usuario.Criar(command.Nome, command.Email, senhaHash);

        await usuarioRepository.AddAsync(usuario);

        var auditLog = AuditLog.Criar(
            usuario.Id,
            "register",
            true,
            "Usuario registrado com sucesso",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        // Token plaintext só vai pro email; persistimos só o hash.
        var confirmationToken = Guid.NewGuid().ToString();
        var confirmationTokenHash = TokenHashHelper.ComputeSha256Hash(confirmationToken);
        var emailToken = EmailConfirmationToken.Criar(usuario.Id, confirmationTokenHash, null, null);
        await emailTokenRepository.AddAsync(emailToken);

        if (emailService is not null)
        {
            try
            {
                var confirmLink = $"{command.BaseUrl.TrimEnd('/')}/auth/confirmar-email?token={Uri.EscapeDataString(confirmationToken)}";
                var body = $@"<html><body style=""font-family:sans-serif;max-width:600px;margin:auto"">
<h2 style=""color:#4f46e5"">Confirme seu email</h2>
<p>Olá {System.Net.WebUtility.HtmlEncode(usuario.Nome)},</p>
<p>Bem-vindo ao EasyStock! Clique no link abaixo para confirmar seu email:</p>
<p><a href=""{confirmLink}"" style=""background:#4f46e5;color:white;padding:12px 24px;text-decoration:none;border-radius:6px;display:inline-block"">Confirmar Email</a></p>
<p>Este link expira em 24 horas.</p>
<p>Se você não se registrou, ignore este email.</p>
</body></html>";

                await emailService.SendAsync(usuario.Email, "Confirme seu email - EasyStock", body, isHtml: true);
                logger.LogInformation("Email de confirmação enviado para {Email}", usuario.Email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao enviar email de confirmação para {Email}. Token gerado normalmente.", usuario.Email);
            }
        }

        await unitOfWork.CommitAsync();

        logger.LogInformation("Usuario cadastrado com sucesso: {UsuarioId}", usuario.Id);
        return new CadastrarUsuarioResult(usuario.Id);
    }
}
