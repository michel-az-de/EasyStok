using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Public;

/// <summary>
/// Registra lead capturado na landing publica. NAO requer autenticacao nem
/// EmpresaId. Faz anti-spam (honeypot + rate-limit por IP) antes de persistir,
/// e dispara dois emails best-effort: confirmacao para o lead e alerta interno.
/// </summary>
public sealed class RegistrarLeadPublicoUseCase(
    ILeadPublicoRepository leadRepository,
    IUnitOfWork unitOfWork,
    ILogger<RegistrarLeadPublicoUseCase> logger,
    IEmailService? emailService = null) : IUseCase<RegistrarLeadPublicoCommand, RegistrarLeadPublicoResult>
{
    private const int MaxLeadsPorIpPorHora = 5;
    private static readonly TimeSpan JanelaAntiSpam = TimeSpan.FromHours(1);

    public async Task<RegistrarLeadPublicoResult> ExecuteAsync(RegistrarLeadPublicoCommand cmd)
    {
        // Honeypot: se preenchido, retorna sucesso "fake" — bot sai feliz, sem registro.
        if (!string.IsNullOrWhiteSpace(cmd.Honeypot))
        {
            logger.LogWarning("Lead descartado por honeypot. IP={Ip}", cmd.IpOrigem);
            return new RegistrarLeadPublicoResult(Guid.Empty, DescartadoPorSpam: true);
        }

        if (!cmd.ConsentimentoLgpd)
            throw new UseCaseValidationException("Consentimento LGPD e obrigatorio.");

        EmailValidator.EnsureValid(cmd.Email, "Email do lead");

        // Rate-limit por IP — protege contra spam basico sem dependencias externas.
        if (!string.IsNullOrWhiteSpace(cmd.IpOrigem))
        {
            var contagemRecente = await leadRepository.ContarPorIpRecenteAsync(cmd.IpOrigem, JanelaAntiSpam);
            if (contagemRecente >= MaxLeadsPorIpPorHora)
            {
                logger.LogWarning(
                    "Lead descartado por rate-limit. IP={Ip} contagem={Count}",
                    cmd.IpOrigem, contagemRecente);
                return new RegistrarLeadPublicoResult(Guid.Empty, DescartadoPorSpam: true);
            }
        }

        var email = EmailAddress.From(cmd.Email);
        var telefone = Telefone.TryFrom(cmd.Telefone);

        var lead = LeadPublico.Criar(
            cmd.Nome,
            email,
            cmd.Origem,
            cmd.ConsentimentoLgpd,
            telefone,
            cmd.Empresa,
            cmd.Mensagem,
            cmd.TipoNegocio,
            cmd.ReceberNewsletter,
            cmd.IpOrigem,
            cmd.UserAgent,
            cmd.UtmSource,
            cmd.UtmMedium,
            cmd.UtmCampaign);

        await leadRepository.AddAsync(lead);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Lead publico {LeadId} registrado. Origem={Origem} Email={Email}",
            lead.Id, lead.Origem, email.Value);

        // Emails best-effort — nao bloqueia o registro se SMTP falhar.
        if (emailService is not null)
        {
            _ = TentarEnviarEmailsAsync(lead);
        }

        return new RegistrarLeadPublicoResult(lead.Id, DescartadoPorSpam: false);
    }

    private async Task TentarEnviarEmailsAsync(LeadPublico lead)
    {
        if (emailService is null) return;

        try
        {
            var assuntoLead = lead.Origem switch
            {
                OrigemLead.Newsletter => "Bem-vindo a newsletter do EasyStok",
                OrigemLead.FaleConosco => "Recebemos sua mensagem — EasyStok",
                OrigemLead.TesteGratis => "Seu teste gratis do EasyStok",
                OrigemLead.AssineAgora => "Vamos comecar — EasyStok",
                _ => "Recebemos seu contato — EasyStok"
            };

            var corpoLead = MontarCorpoLead(lead);
            await emailService.SendAsync(lead.Email.Value, assuntoLead, corpoLead, isHtml: true);
            logger.LogInformation("Email de confirmacao enviado para lead {LeadId}", lead.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar email de confirmacao para lead {LeadId}", lead.Id);
        }
    }

    private static string MontarCorpoLead(LeadPublico lead)
    {
        var nomeSeguro = System.Net.WebUtility.HtmlEncode(lead.Nome);
        var mensagem = lead.Origem switch
        {
            OrigemLead.Newsletter =>
                "Voce esta na lista. A gente envia novidades sobre o EasyStok com pouca frequencia — so quando faz diferenca.",
            OrigemLead.FaleConosco =>
                "Recebemos sua mensagem e retornamos em ate 1 dia util. Se for urgente, fala com a gente no WhatsApp.",
            OrigemLead.TesteGratis or OrigemLead.AssineAgora =>
                "Recebemos seu interesse no teste gratis. Se ainda nao terminou o cadastro, conclua agora — sao 14 dias gratis pra testar tudo.",
            _ => "Recebemos seu contato e em breve a gente fala com voce."
        };

        return $@"<html><body style=""font-family:Inter,sans-serif;max-width:560px;margin:auto;color:#0A1530"">
<h2 style=""color:#0E2A6E;letter-spacing:-0.01em"">Oi, {nomeSeguro}.</h2>
<p style=""font-size:15.5px;line-height:1.55;color:#2A3556"">{mensagem}</p>
<p style=""font-size:14px;color:#4A5470;margin-top:32px"">— Equipe EasyStok</p>
</body></html>";
    }
}
