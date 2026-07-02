using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.UseCases.CadastrarUsuario;

public sealed class CadastrarUsuarioUseCase(
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    IEmailConfirmationTokenRepository emailTokenRepository,
    IEmailService? emailService,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
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

        // Nunca confiar no BaseUrl do corpo: so montamos o link de confirmacao quando
        // o host bate com uma origem confiavel (#765). Host nao-confiavel => nao envia
        // link (o token fica persistido; reenvio server-side pode ser disparado depois).
        var baseUrlConfiavel = LinkBaseUrlResolver.ResolveTrusted(command.BaseUrl, configuration);

        if (emailService is not null && baseUrlConfiavel is not null)
        {
            try
            {
                var confirmLink = $"{baseUrlConfiavel}/auth/confirmar-email?token={Uri.EscapeDataString(confirmationToken)}";
                var body = string.Format(
                    EasyStock.Application.Resources.EmailTemplateLoader.LoadBody("cadastro_usuario_confirmacao"),
                    System.Net.WebUtility.HtmlEncode(usuario.Nome),
                    confirmLink);

                await emailService.SendAsync(usuario.Email, "Confirme seu email - EasyStock", body, isHtml: true);
                logger.LogInformation("Email de confirmação enviado para {Email}", usuario.Email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao enviar email de confirmação para {Email}. Token gerado normalmente.", usuario.Email);
            }
        }
        else if (emailService is not null && !string.IsNullOrEmpty(command.BaseUrl))
        {
            logger.LogWarning("BaseUrl de confirmação de cadastro ignorado por não estar na allowlist de origens confiáveis. Email de confirmação não enviado para {Email}.", usuario.Email);
        }

        await unitOfWork.CommitAsync();

        logger.LogInformation("Usuario cadastrado com sucesso: {UsuarioId}", usuario.Id);
        return new CadastrarUsuarioResult(usuario.Id);
    }
}
