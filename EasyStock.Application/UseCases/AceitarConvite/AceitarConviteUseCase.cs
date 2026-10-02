using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Validators;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.AceitarConvite;

/// <summary>Aceite do convite (N9). <paramref name="Ip"/> e <paramref name="UserAgent"/> vêm da conexão, nunca do corpo.</summary>
public sealed record AceitarConviteCommand(string Token, string NovaSenha, string? Ip = null, string? UserAgent = null) : ICommand;

public sealed record AceitarConviteResult(bool Success);

/// <summary>
/// Aceite do convite de primeiro acesso (N9): quem consome é o POST, nunca o GET. O uso único é de verdade
/// (<c>ConsumirAsync</c> é um UPDATE condicional e só a linha afetada igual a 1 autoriza definir a senha: dois POST
/// simultâneos, um vence). Define a senha pela política existente, <b>verifica o canal do token</b> (e-mail marca
/// <c>EmailConfirmado</c>; WhatsApp marca o telefone verificado e grava os opt-ins de Segurança e Operacional, como a
/// verificação administrativa da N4), grava a via já mascarada e revoga os outros convites. Convite revogado, usado,
/// vencido, de outra finalidade, de superadmin ou de conta inativa recebem a mesma recusa. Não emite sessão: a pessoa
/// volta ao login. Nenhum log leva token, e-mail, telefone ou senha: só o <c>UsuarioId</c>.
/// </summary>
public sealed class AceitarConviteUseCase(
    IResetTokenRepository resetTokenRepository,
    IUsuarioRepository usuarioRepository,
    IConsentimentoRepository consentimentoRepository,
    IAuditLogRepository auditLogRepository,
    ConvitesDeAcesso convites,
    LimitePedidosAcesso limitePorIp,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<AceitarConviteUseCase> logger) : IUseCase<AceitarConviteCommand, AceitarConviteResult>
{
    internal const string MensagemDeRecusa = "Convite inválido ou expirado.";

    public async Task<AceitarConviteResult> ExecuteAsync(AceitarConviteCommand command)
    {
        await limitePorIp.ExigirAsync(command.Ip);

        // A política de senha vem antes do consumo: quem errou a senha tenta de novo com o mesmo link.
        var validacao = new AceitarConviteCommandValidator().Validate(command);
        if (!validacao.IsValid)
            throw new UseCaseValidationException(string.Join(" ", validacao.Errors.Select(e => e.ErrorMessage)));

        var agora = relogio.GetUtcNow().UtcDateTime;

        var token = await resetTokenRepository.GetByTokenAsync(command.Token);
        if (token is null || !token.ServePara(FinalidadeResetToken.Convite) || token.Usado || token.ExpiraEm <= agora)
            throw Recusar("token inexistente, usado, vencido ou de outra finalidade");

        var usuario = await usuarioRepository.GetByIdAsync(token.UsuarioId);
        if (usuario is null || !usuario.Ativo || !usuario.ConvitePendente || usuario.EhSuperAdmin())
            throw Recusar($"usuario {token.UsuarioId} inexistente, inativo, ja aceitou ou superadmin");

        var porWhatsApp = token.Canal == ConvitesDeAcesso.CanalWhatsApp;
        if (porWhatsApp && usuario.Telefone is null)
            throw Recusar($"usuario {usuario.Id} sem telefone para verificar");

        // Consome ANTES de mexer na conta: quem perde a corrida do UPDATE sai aqui, sem tocar em nada.
        if (!await resetTokenRepository.ConsumirAsync(token.Id, agora))
            throw Recusar($"convite do usuario {usuario.Id} ja consumido");

        var senhaHash = passwordHasher.Hash(command.NovaSenha);
        if (porWhatsApp)
        {
            usuario.AceitarConvite(senhaHash, ViaDoConvite.WhatsApp(usuario.Telefone!), agora);
            usuario.MarcarTelefoneVerificado(agora);
            await GravarOptInsDaVerificacaoAsync(usuario, command.Ip);
        }
        else
        {
            usuario.AceitarConvite(senhaHash, ViaDoConvite.Email, agora);
            usuario.EmailConfirmado = true;
        }

        await usuarioRepository.UpdateAsync(usuario);

        // O convite do outro canal (e qualquer reenvio anterior) morre junto: um aceite só.
        await convites.RevogarAbertosAsync(usuario.Id);

        await auditLogRepository.AddAsync(AuditLog.Criar(
            usuario.Id, "aceitar-convite", true,
            $"Convite aceito por {(porWhatsApp ? "WhatsApp" : "e-mail")}; senha definida e canal verificado.",
            command.Ip, command.UserAgent));
        await unitOfWork.CommitAsync();

        logger.LogInformation("Convite aceito pelo usuario {UsuarioId} (via WhatsApp: {PorWhatsApp})", usuario.Id, porWhatsApp);
        return new AceitarConviteResult(true);
    }

    /// <summary>Como a verificação administrativa da N4: Segurança e Operacional, com data e IP; a autoria é o próprio convite.</summary>
    private async Task GravarOptInsDaVerificacaoAsync(Usuario usuario, string? ip)
    {
        foreach (var categoria in new[] { CategoriaConteudoNotificacao.Seguranca, CategoriaConteudoNotificacao.Operacional })
            await consentimentoRepository.AddAsync(ConsentimentoNotificacao.Registrar(
                usuario.Id, CanalNotificacao.WhatsApp, categoria, optIn: true,
                atualizadoPor: $"convite:{usuario.Id}", ipOrigem: ip));
    }

    private RegraDeDominioVioladaException Recusar(string motivo)
    {
        logger.LogWarning("Aceite de convite recusado: {Motivo}", motivo);
        return new RegraDeDominioVioladaException(MensagemDeRecusa);
    }
}
