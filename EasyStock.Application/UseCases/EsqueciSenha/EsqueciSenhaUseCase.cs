using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.UseCases.EsqueciSenha;

public sealed class EsqueciSenhaUseCase(
    IUsuarioRepository usuarioRepository,
    IResetTokenRepository resetTokenRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
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
                // Nunca confiar no BaseUrl do corpo da requisicao: so compomos o link
                // quando o host bate com uma origem confiavel (#765). Host nao-confiavel
                // (ou ausente) => e-mail com token puro, nunca link para host do atacante.
                var baseUrlConfiavel = LinkBaseUrlResolver.ResolveTrusted(command.BaseUrl, configuration);
                if (!string.IsNullOrEmpty(command.BaseUrl) && baseUrlConfiavel is null)
                    logger.LogWarning("BaseUrl do reset de senha ignorado por nao estar na allowlist de origens confiaveis.");

                var resetLink = baseUrlConfiavel is not null
                    ? $"{baseUrlConfiavel}/auth/redefinir-senha?token={Uri.EscapeDataString(token)}"
                    : token;

                var hasLink = baseUrlConfiavel is not null;
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