using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Net;

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

        var usuario = await usuarioRepository.GetByEmailAsync(command.Email);
        if (usuario == null || !usuario.Ativo)
        {
            logger.LogWarning("Tentativa de esqueci senha para email inexistente: {Email}", command.Email);
            return new EsqueciSenhaResult(true);
        }

        var token = Guid.NewGuid().ToString();
        var expiraEm = DateTime.UtcNow.AddHours(1);
        var resetToken = ResetToken.Criar(
            usuario.Id,
            token,
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
                var body = MontarEmailRecuperacaoSenha(usuario.Nome, resetLink, token, hasLink);

                await emailService.SendAsync(usuario.Email, subject, body, isHtml: true);
                logger.LogInformation("E-mail de recuperação de senha enviado para {Email}", usuario.Email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao enviar e-mail de recuperação de senha para {Email}. Token gerado normalmente.", usuario.Email);
            }
        }
        else
        {
            logger.LogInformation("Token de reset gerado para usuário {UsuarioId}", usuario.Id);
        }

        return new EsqueciSenhaResult(true);
    }

    private static string MontarEmailRecuperacaoSenha(string nome, string resetLink, string token, bool hasLink)
    {
        var nomeSeguro = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(nome) ? "Olá" : nome.Trim());
        var acaoHtml = hasLink
            ? $"""
              <a href="{WebUtility.HtmlEncode(resetLink)}" style="display:inline-block;background:#4f46e5;color:#ffffff;text-decoration:none;font-weight:700;padding:14px 20px;border-radius:12px;">Criar nova senha</a>
              <p style="margin:18px 0 0;color:#64748b;font-size:13px;line-height:1.6;">Se o botão não funcionar, copie e cole este link no navegador:<br><span style="word-break:break-all;color:#334155;">{WebUtility.HtmlEncode(resetLink)}</span></p>
              """
            : $"""
              <p style="margin:0 0 10px;color:#334155;font-size:14px;line-height:1.6;">Use este token para criar uma nova senha:</p>
              <div style="font-family:Consolas,Menlo,monospace;background:#eef2ff;color:#3730a3;border:1px solid #c7d2fe;border-radius:12px;padding:14px 16px;font-weight:700;letter-spacing:.02em;word-break:break-all;">{WebUtility.HtmlEncode(token)}</div>
              """;

        return $$"""
          <!doctype html>
          <html lang="pt-BR">
          <body style="margin:0;background:#f4f7fb;font-family:Inter,Segoe UI,Arial,sans-serif;color:#0f172a;">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f7fb;padding:32px 16px;">
              <tr>
                <td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border:1px solid #e2e8f0;border-radius:20px;overflow:hidden;box-shadow:0 18px 46px rgba(15,23,42,.08);">
                    <tr>
                      <td style="padding:28px 30px;background:linear-gradient(135deg,#312e81,#4f46e5 58%,#0891b2);color:#ffffff;">
                        <div style="font-size:12px;font-weight:800;letter-spacing:.18em;text-transform:uppercase;opacity:.72;">EasyStock</div>
                        <h1 style="margin:10px 0 0;font-size:26px;line-height:1.2;">Redefinição de senha</h1>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:30px;">
                        <p style="margin:0 0 14px;font-size:16px;line-height:1.6;">Olá, {{nomeSeguro}}.</p>
                        <p style="margin:0 0 22px;color:#475569;font-size:14px;line-height:1.7;">Recebemos uma solicitação para redefinir a senha da sua conta. O acesso abaixo é válido por <strong>1 hora</strong>.</p>
                        {{acaoHtml}}
                        <div style="margin-top:26px;padding:16px;border-radius:14px;background:#f8fafc;border:1px solid #e2e8f0;color:#64748b;font-size:13px;line-height:1.6;">
                          Se você não solicitou a redefinição de senha, ignore este e-mail. Sua senha atual continuará a mesma.
                        </div>
                        <p style="margin:24px 0 0;color:#64748b;font-size:13px;line-height:1.6;">Equipe EasyStock</p>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
          </body>
          </html>
          """;
    }
}
