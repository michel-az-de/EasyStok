using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.ContatoUsuario;

public sealed record VerificarTelefoneUsuarioCommand(Guid UsuarioId, string? Motivo) : ICommand;

public sealed record VerificarTelefoneUsuarioResult(Guid UsuarioId, string Telefone, DateTime VerificadoEm);

/// <summary>
/// Verificação administrativa do telefone (N4, só SuperAdmin): serve à equipe pequena (a dona e o Felipe) enquanto a
/// verificação por código no WhatsApp espera a N6 e a N8. Exige o motivo (10 caracteres), grava os consentimentos
/// explícitos de WhatsApp (<c>Seguranca</c> e <c>Operacional</c>, com data e o IP do superadmin, que fica em
/// <c>AtualizadoPor</c>) e audita. A pessoa não deu o aceite no aparelho dela e o revoga em Preferências.
/// </summary>
public sealed class VerificarTelefoneUsuarioUseCase(
    IUsuarioRepository usuarioRepository,
    IConsentimentoRepository consentimentoRepository,
    IAuditLogRepository auditLogRepository,
    ICurrentUserAccessor currentUserAccessor,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<VerificarTelefoneUsuarioUseCase> logger)
    : IUseCase<VerificarTelefoneUsuarioCommand, VerificarTelefoneUsuarioResult>
{
    public const int TamanhoMinimoDoMotivo = 10;

    public async Task<VerificarTelefoneUsuarioResult> ExecuteAsync(VerificarTelefoneUsuarioCommand command)
    {
        var motivo = command.Motivo?.Trim();
        if (string.IsNullOrEmpty(motivo) || motivo.Length < TamanhoMinimoDoMotivo)
            throw new UseCaseValidationException(
                "MOTIVO_OBRIGATORIO", $"Informe o motivo da verificação (mínimo de {TamanhoMinimoDoMotivo} caracteres).");

        var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
            ?? throw new UseCaseValidationException("USUARIO_NAO_ENCONTRADO", "Usuário não encontrado.");
        if (!usuario.Ativo || usuario.Telefone is null)
            throw new UseCaseValidationException(
                "TELEFONE_AUSENTE", "O usuário precisa estar ativo e ter informado o telefone antes da verificação.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        var executor = currentUserAccessor.UsuarioId;
        var ip = currentUserAccessor.Ip;
        usuario.MarcarTelefoneVerificado(agora);
        await usuarioRepository.UpdateAsync(usuario);

        // Histórico imutável: cada mudança é um registro novo, e o mais recente é o estado atual.
        foreach (var categoria in new[] { CategoriaConteudoNotificacao.Seguranca, CategoriaConteudoNotificacao.Operacional })
            await consentimentoRepository.AddAsync(ConsentimentoNotificacao.Registrar(
                usuario.Id, CanalNotificacao.WhatsApp, categoria, optIn: true,
                atualizadoPor: $"superadmin:{executor}", ipOrigem: ip));

        await auditLogRepository.AddAsync(AuditLog.Criar(
            usuario.Id, "telefone-verificado", true,
            $"Telefone verificado pelo superadmin {executor}. Motivo: {motivo}", ip, currentUserAccessor.UserAgent));
        await unitOfWork.CommitAsync();

        logger.LogInformation("AUDIT: telefone do usuario {UsuarioId} verificado pelo superadmin {Executor}", usuario.Id, executor);
        return new VerificarTelefoneUsuarioResult(usuario.Id, usuario.Telefone.Value, agora);
    }
}
